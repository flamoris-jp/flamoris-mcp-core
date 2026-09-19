using System.Diagnostics;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

public sealed class RequestContext
{
    private readonly IMcpHost host;
    private readonly CapabilityGrant grant;
    private readonly RequestGuard? guard;
    private readonly bool mutation;
    private readonly CancellationToken token;
    private readonly long started = Stopwatch.GetTimestamp();
    private readonly int timeoutMs;
    private bool sealedRequest;
    private McpResult? committed;
    internal RequestContext(IMcpHost host, CapabilityGrant grant, RequestGuard? guard,
        bool mutation, CancellationToken token, int timeoutMs)
    { this.host = host; this.grant = grant; this.guard = guard; this.mutation = mutation; this.token = token; this.timeoutMs = timeoutMs; }

    // Host operations run here on the SAME serialization lane as human gestures.
    public Task<JsonElement> ReadAsync(Func<JsonElement> query) => host.InvokeAsync(() =>
    {
        lock (grant.Gate) { Demand(); return query(); }
    }, token);

    // Exactly one synchronous, atomic host transaction/history operation per request.
    // Preparation must have no persistent side effects. The callback must rollback on failure.
    public Task<JsonElement> CommitAsync(Func<JsonElement> commit) => host.InvokeAsync(() =>
    {
        lock (grant.Gate)
        {
            if (!mutation || committed is not null) throw new McpFault(McpErrors.Forbidden);
            Demand();
            JsonElement value = commit().Clone();
            committed = new(value);
            sealedRequest = true;
            return value;
        }
    }, token);

    private void Demand()
    {
        if (sealedRequest) throw new McpFault(McpErrors.Cancelled);
        grant.Demand();
        if (Stopwatch.GetElapsedTime(started).TotalMilliseconds >= timeoutMs) throw new McpFault(McpErrors.Timeout);
        token.ThrowIfCancellationRequested();
        var current = host.Snapshot;
        if (!current.Available) throw new McpFault(McpErrors.HostUnavailable);
        if (current.RuntimeId != grant.Snapshot.RuntimeId || current.ProductId != grant.Snapshot.ProductId)
            throw new McpFault(McpErrors.StaleSession);
        if (current.DocumentToken != grant.Snapshot.DocumentToken) throw new McpFault(McpErrors.StaleDocument);
        if (guard is not null)
        {
            if (guard.RuntimeId != current.RuntimeId) throw new McpFault(McpErrors.StaleSession);
            if (guard.DocumentToken != current.DocumentToken) throw new McpFault(McpErrors.StaleDocument);
            if (guard.ExpectedRevision is { } revision && revision != current.Revision) throw new McpFault(McpErrors.StaleRevision);
        }
        if (mutation && (grant.Permission != McpPermission.Edit)) throw new McpFault(McpErrors.Forbidden);
        if (mutation && guard?.ExpectedRevision is null) throw new McpFault(McpErrors.InvalidRequest);
        if (current.HumanEditing) throw new McpFault(McpErrors.Busy);
    }
    internal Task ValidateAsync() => host.InvokeAsync(() => { lock (grant.Gate) Demand(); return true; }, token);
    internal McpResult Finish(McpResult result)
    {
        lock (grant.Gate)
        {
            sealedRequest = true;
            return committed ?? result;
        }
    }
}
