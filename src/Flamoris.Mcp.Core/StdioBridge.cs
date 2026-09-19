using System.IO.Pipes;
using System.Text;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

public static class StdioBridge
{
    public const string CredentialEnvironmentVariable = "FLAMORIS_MCP_CAPABILITY";
    public static async Task RunAsync(string pipeName, string? capability, Stream input, Stream output,
        McpOptions? options = null, CancellationToken cancellationToken = default)
    {
        options ??= new();
        options.Validate();
        if (!McpOptions.ValidPipeName(pipeName)) throw new McpFault(McpErrors.InvalidRequest);
        if (capability is not { Length: 64 }) throw new McpFault(McpErrors.Unauthorized);
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        await using var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try { await pipe.ConnectAsync(5000, lifetime.Token); }
        catch (Exception e) when (e is TimeoutException or IOException)
        { throw new McpFault(McpErrors.HostUnavailable); }
        using (var handshake = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token))
        {
            handshake.CancelAfter(TimeSpan.FromSeconds(5));
            await PipeHandshake.WriteAsync(pipe, new { version = 1, capability }, handshake.Token);
            using var reply = JsonDocument.Parse(await PipeHandshake.ReadAsync(pipe, handshake.Token));
            if (reply.RootElement.TryGetProperty("error", out _)) throw new McpFault(McpErrors.Unauthorized);
            if (!reply.RootElement.TryGetProperty("version", out var version) || version.GetInt32() != 1)
                throw new McpFault(McpErrors.TransportUnavailable);
        }
        Task inbound = PumpAsync(input, pipe, options, lifetime.Token);
        Task outbound = PumpAsync(pipe, output, options, lifetime.Token);
        Task first = await Task.WhenAny(inbound, outbound);
        lifetime.Cancel();
        pipe.Dispose();
        // A platform's console stdin may ignore cancellation. It cannot keep the executable alive.
        try { await Task.WhenAll(inbound, outbound).WaitAsync(TimeSpan.FromSeconds(2)); }
        catch (Exception e) when (e is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException) { }
        if (first.IsFaulted) throw new McpFault(McpErrors.TransportUnavailable);
    }
    private static async Task PumpAsync(Stream input, Stream output, McpOptions options, CancellationToken token)
    {
        var reader = new BoundedLineReader(input, options.MaxRequestBytes);
        while (!token.IsCancellationRequested)
        {
            using var read = CancellationTokenSource.CreateLinkedTokenSource(token);
            read.CancelAfter(options.ReadTimeoutMs);
            var frame = await reader.ReadAsync(read.Token);
            if (frame.Status == McpFrameStatus.EndOfStream) return;
            if (frame.Status != McpFrameStatus.Success) throw new McpFault(McpErrors.InvalidRequest);
            using var write = CancellationTokenSource.CreateLinkedTokenSource(token);
            write.CancelAfter(options.WriteTimeoutMs);
            await output.WriteAsync(Encoding.UTF8.GetBytes(frame.Line! + "\n"), write.Token);
            await output.FlushAsync(write.Token);
        }
    }
}
