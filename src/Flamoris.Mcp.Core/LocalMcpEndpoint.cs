using System.IO.Pipes;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

public sealed class LocalMcpEndpoint(McpBoundary boundary)
{
    private int running;
    public async Task RunAsync(CapabilityGrant grant, CancellationToken cancellationToken = default)
    {
        if (!boundary.Owns(grant)) throw new McpFault(McpErrors.Unauthorized);
        if (Interlocked.Exchange(ref running, 1) != 0) throw new McpFault(McpErrors.Busy);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(grant.Revoked, cancellationToken);
        try
        {
            while (!lifetime.IsCancellationRequested)
            {
                await using NamedPipeServerStream pipe = OperatingSystem.IsWindows()
                    ? WindowsLocalPipe.Create(boundary.Options.PipeName)
                    : new(boundary.Options.PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                using var close = lifetime.Token.Register(() => { try { pipe.Dispose(); } catch { } });
                boundary.SetConnection(grant, true, false);
                try
                {
                    await pipe.WaitForConnectionAsync(lifetime.Token);
                    using var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    handshake.CancelAfter(TimeSpan.FromSeconds(5));
                    string line = await PipeHandshake.ReadAsync(pipe, handshake.Token);
                    using var json = JsonDocument.Parse(line, new() { MaxDepth = 4 });
                    var root = json.RootElement;
                    bool valid = root.ValueKind == JsonValueKind.Object && root.EnumerateObject().Count() == 2
                        && root.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.Number
                        && version.TryGetInt32(out var n) && n == 1
                        && root.TryGetProperty("capability", out var secret) && secret.ValueKind == JsonValueKind.String
                        && grant.Authenticate(secret.GetString());
                    if (!valid)
                    {
                        await PipeHandshake.WriteAsync(pipe, new { error = McpErrors.Unauthorized }, handshake.Token);
                        boundary.Diagnostics.Event("mcp.auth", McpErrors.Unauthorized);
                        continue;
                    }
                    await PipeHandshake.WriteAsync(pipe, new { version = 1 }, handshake.Token);
                    boundary.SetConnection(grant, true, true);
                    boundary.Diagnostics.Event("mcp.transport", "connected");
                    await McpProtocol.ServeAsync(pipe, boundary, grant, lifetime.Token);
                }
                catch (Exception e) when (e is IOException or OperationCanceledException or ObjectDisposedException
                    or JsonException or McpFault or UnauthorizedAccessException)
                {
                    boundary.SetConnection(grant, false, false, McpErrors.TransportUnavailable);
                    boundary.Diagnostics.Event("mcp.transport", McpErrors.TransportUnavailable);
                }
                finally { boundary.SetConnection(grant, false, false); }
            }
        }
        catch (Exception)
        {
            boundary.Diagnostics.Event("mcp.transport", McpErrors.TransportUnavailable);
            boundary.SetConnection(grant, false, false, McpErrors.TransportUnavailable);
        }
        finally { Interlocked.Exchange(ref running, 0); boundary.SetConnection(grant, false, false); }
    }
}
