using System.Diagnostics;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

public sealed class McpBoundary : IDisposable
{
    private readonly IMcpHost host;
    private readonly object lifecycle = new();
    private readonly SemaphoreSlim admission;
    private CapabilityGrant? grant;
    private long generation;
    private bool disposed;
    public McpOptions Options { get; }
    public StatusProjection Status { get; } = new();
    public IReadOnlyDictionary<string, HostTool> Tools { get; }
    internal McpDiagnostics Diagnostics { get; }
    public McpBoundary(IMcpHost host, IEnumerable<HostTool> tools, McpOptions options, McpDiagnostics diagnostics)
    {
        options.Validate();
        this.host = host; Options = options; Diagnostics = diagnostics;
        var registry = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);
        if (registry.Count > 512) throw new ArgumentException("Too many tools.");
        Tools = new System.Collections.ObjectModel.ReadOnlyDictionary<string, HostTool>(registry);
        admission = new(options.MaxConcurrentRequests);
        host.Invalidating += Disable;
    }
    // Explicit opt-in even if a host's persisted Enabled preference is true.
    public async Task<CapabilityGrant> EnableAsync(McpPermission permission, CancellationToken token = default)
    {
        if (!Enum.IsDefined(permission)) throw new ArgumentException("Unknown permission.");
        long expected;
        lock (lifecycle) { ObjectDisposedException.ThrowIf(disposed, this); expected = generation; }
        return await host.InvokeAsync(() =>
        {
            lock (lifecycle)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                if (expected != generation) throw new McpFault(McpErrors.StaleSession);
                var snapshot = host.Snapshot;
                if (!snapshot.Available || string.IsNullOrWhiteSpace(snapshot.DocumentToken))
                    throw new McpFault(McpErrors.HostUnavailable);
                grant?.Revoke();
                grant = new(snapshot, permission); generation++;
                Status.Connection(true, false, false);
                Diagnostics.Event("mcp.auth", "enabled");
                return grant;
            }
        }, token);
    }
    public void Disable()
    {
        lock (lifecycle)
        {
            generation++; grant?.Revoke(); grant = null;
            Status.Connection(false, false, false);
        }
        Diagnostics.Event("mcp.auth", "revoked");
    }
    internal void SetConnection(CapabilityGrant attachment, bool available, bool connected, string? error = null)
    {
        lock (lifecycle)
            if (ReferenceEquals(grant, attachment) && attachment.IsActive)
                Status.Connection(true, available, connected, error);
    }
    internal bool Owns(CapabilityGrant attachment)
    { lock (lifecycle) return ReferenceEquals(grant, attachment) && attachment.IsActive; }

    public async Task<McpResult> InvokeAsync(CapabilityGrant attachment, string toolName,
        JsonElement input, RequestGuard? guard, CancellationToken cancellationToken = default)
    {
        if (!Owns(attachment)) return McpResult.Failure(McpErrors.Unauthorized);
        if (!Tools.TryGetValue(toolName, out var tool)) return McpResult.Failure(McpErrors.UnsupportedCapability);
        if (tool.Kind != OperationKind.Query && attachment.Permission != McpPermission.Edit)
            return McpResult.Failure(McpErrors.Forbidden);
        if (!admission.Wait(0)) return McpResult.Failure(McpErrors.Busy);
        var started = Stopwatch.GetTimestamp();
        var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, attachment.Revoked);
        deadline.CancelAfter(Options.RequestTimeoutMs);
        var context = new RequestContext(host, attachment, guard, tool.Kind != OperationKind.Query,
            deadline.Token, Options.RequestTimeoutMs);
        using var activity = tool.Foreground && Options.ShowActivityCursor ? Status.BeginForeground() : null;
        Task<JsonElement>? work = null;
        McpResult result;
        try
        {
            // Host code cannot block the transport reader or prevent deadline observation.
            work = Task.Run(async () =>
            {
                await context.ValidateAsync();
                var value = await tool.ExecuteAsync(context, input, deadline.Token);
                // Guard disclosure too; committed mutations instead return their committed result.
                if (tool.Kind == OperationKind.Query) await context.ValidateAsync();
                return value;
            }, CancellationToken.None);
            result = new(await work.WaitAsync(deadline.Token));
        }
        catch (OperationCanceledException)
        {
            result = McpResult.Failure(!attachment.IsActive ? McpErrors.Unauthorized :
                cancellationToken.IsCancellationRequested ? McpErrors.Cancelled : McpErrors.Timeout);
        }
        catch (McpFault fault) { result = McpResult.Failure(fault.Code); }
        catch { result = McpResult.Failure(McpErrors.InternalError); }
        result = context.Finish(result);
        // Keep the slot occupied by uncooperative preparation until it actually terminates.
        if (work is { IsCompleted: false })
            _ = work.ContinueWith(t => { _ = t.Exception; deadline.Dispose(); admission.Release(); },
                CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        else { deadline.Dispose(); admission.Release(); }
        Diagnostics.Event(tool.Kind == OperationKind.Query ? "mcp.query" : "mcp.command",
            result.Error ?? "success", tool.Name, (long)Stopwatch.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }
    public void Dispose()
    {
        lock (lifecycle) { if (disposed) return; disposed = true; }
        host.Invalidating -= Disable;
        Disable();
        // Outstanding workers still own admission slots. Do not dispose under them.
    }
}
