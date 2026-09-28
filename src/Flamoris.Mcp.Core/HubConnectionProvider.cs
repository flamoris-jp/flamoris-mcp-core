using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace Flamoris.Mcp.Core;

public sealed record HubConnectionSettings(Uri Endpoint)
{
    public void Validate()
    {
        if (!Endpoint.IsAbsoluteUri || Endpoint.Scheme != "wss" || !string.IsNullOrEmpty(Endpoint.UserInfo)
            || !string.IsNullOrEmpty(Endpoint.Query) || !string.IsNullOrEmpty(Endpoint.Fragment))
            throw new ArgumentException("Hub endpoint must be WSS without credentials, query or fragment.");
    }
}

/// <summary>Outbound-only Hub transport over the same host boundary and transient grant.</summary>
public sealed class HubConnectionProvider(HubConnectionSettings settings, McpBoundary boundary,
    CapabilityGrant grant, Func<CancellationToken, ValueTask<string>> credential) : IMcpConnectionProvider
{
    public string Id => "flamoris-mcp-hub";
    internal Func<IHubSocket> SocketFactory { get; init; } = () => new HubSocket();
    private CancellationTokenSource? lifetime;
    private Task? worker;
    private const int MaxBytes = 4 * 1024 * 1024;
    private static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web);

    public ValueTask StartAsync(CancellationToken cancellationToken)
    {
        settings.Validate();
        cancellationToken.ThrowIfCancellationRequested();
        if (!boundary.Owns(grant)) throw new McpFault(McpErrors.Unauthorized);
        if (worker is { IsCompleted: false }) return ValueTask.CompletedTask;
        lifetime?.Dispose();
        lifetime = CancellationTokenSource.CreateLinkedTokenSource(grant.Revoked);
        worker = RunAsync(lifetime.Token);
        return ValueTask.CompletedTask;
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    { await StopAsync(CancellationToken.None); await StartAsync(cancellationToken); }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        lifetime?.Cancel();
        if (worker is not null) await worker.ConfigureAwait(false);
        worker = null;
        lifetime?.Dispose(); lifetime = null;
    }

    private async Task RunAsync(CancellationToken token)
    {
        for (int failures = 0; failures < 6 && !token.IsCancellationRequested; failures++)
        {
            try { await ConnectAsync(token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch { boundary.SetConnection(grant, false, false, "hub_unavailable"); }
            if (failures < 5)
                try { await Task.Delay(TimeSpan.FromSeconds(Math.Min(1 << failures, 16)), token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
        }
        boundary.SetConnection(grant, false, false, token.IsCancellationRequested ? null : "hub_unavailable");
    }

    private async Task ConnectAsync(CancellationToken token)
    {
        using var socket = SocketFactory();
        using var attempt = CancellationTokenSource.CreateLinkedTokenSource(token);
        using var abort = token.Register(socket.Abort);
        string secret = await credential(token).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(secret) || secret.Length > 4096 || secret.Any(char.IsControl))
            throw new McpFault(McpErrors.Unauthorized);

        attempt.CancelAfter(TimeSpan.FromSeconds(15));
        await socket.ConnectAsync(settings.Endpoint, secret, attempt.Token).ConfigureAwait(false);
        var catalog = boundary.Tools.Values
            .Where(t => t.Kind == OperationKind.Query || grant.Permission == McpPermission.Edit)
            .Select(McpProtocol.Describe).Select(t => new { name = t.Name, inputSchema = t.InputSchema }).ToList();
        catalog.Insert(0, new { name = "mcp.context", inputSchema = JsonSerializer.SerializeToElement(
            new { type = "object", properties = new { }, additionalProperties = false }) });
        await Send(socket, new { type = "register", version = 1, productId = grant.Snapshot.ProductId,
            runtimeId = grant.Snapshot.RuntimeId, documentToken = grant.Snapshot.DocumentToken,
            permission = grant.Permission == McpPermission.Edit ? "edit" : "readOnly", tools = catalog }, attempt.Token).ConfigureAwait(false);
        using var ready = await Receive(socket, attempt.Token).ConfigureAwait(false);
        if (ready.RootElement.GetProperty("type").GetString() != "ready"
            || ready.RootElement.GetProperty("version").GetInt32() != 1) throw new McpFault(McpErrors.InvalidRequest);
        boundary.SetConnection(grant, true, true);
        // Keep receiving while executing so disconnect/cancellation reaches the commit guard.
        using var receiveLifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task<JsonDocument> incoming = Receive(socket, receiveLifetime.Token);
        try
        {
        while (!token.IsCancellationRequested)
        {
            using var message = await incoming.ConfigureAwait(false);
            var root = message.RootElement;
            if (root.GetProperty("type").GetString() != "call") throw new McpFault(McpErrors.InvalidRequest);
            string id = root.GetProperty("id").GetString() ?? "";
            if (id.Length is < 1 or > 128) throw new McpFault(McpErrors.InvalidRequest);
            string name = root.GetProperty("name").GetString() ?? "";
            JsonElement args = root.GetProperty("arguments").Clone();
            using var request = CancellationTokenSource.CreateLinkedTokenSource(token);
            incoming = Receive(socket, receiveLifetime.Token);
            var call = Invoke(name, args, request.Token);
            if (await Task.WhenAny(call, incoming).ConfigureAwait(false) == incoming)
            {
                request.Cancel(); // disconnect or invalid pipelining: never replay the outstanding call
                try { await call.ConfigureAwait(false); } catch { }
                using var unexpected = await incoming.ConfigureAwait(false);
                throw new McpFault(McpErrors.InvalidRequest);
            }
            var result = await call.ConfigureAwait(false);
            await Send(socket, new { type = "result", id, result }, token).ConfigureAwait(false);
        }
        }
        finally
        {
            receiveLifetime.Cancel(); socket.Abort();
            try { using var pending = await incoming.ConfigureAwait(false); } catch { }
            boundary.SetConnection(grant, false, false);
        }
    }

    private async Task<object> Invoke(string name, JsonElement args, CancellationToken token)
    {
        McpResult result;
        try
        {
            if (args.ValueKind != JsonValueKind.Object) throw new McpFault(McpErrors.InvalidRequest);
            if (name == "mcp.context")
            {
                if (args.EnumerateObject().Any()) throw new McpFault(McpErrors.InvalidRequest);
                result = new(await boundary.ContextAsync(grant, token).ConfigureAwait(false));
            }
            else
            {
                if (args.EnumerateObject().Any(p => p.Name is not ("input" or "guard"))
                    || !args.TryGetProperty("input", out var input)) throw new McpFault(McpErrors.InvalidRequest);
                var guard = args.TryGetProperty("guard", out var g) ? McpProtocol.ParseGuard(g) : null;
                result = await boundary.InvokeAsync(grant, name, input, guard, token).ConfigureAwait(false);
            }
        }
        catch (McpFault e) { result = McpResult.Failure(e.Code); }
        catch (OperationCanceledException) { result = McpResult.Failure(McpErrors.Cancelled); }
        catch { result = McpResult.Failure(McpErrors.InvalidRequest); }
        // Explicit MCP wire names; never serialize framework implementation members.
        JsonElement value = result.Error is { } code ? JsonSerializer.SerializeToElement(new { error = new { code } })
            : result.Value ?? JsonSerializer.SerializeToElement(new { });
        return new { isError = result.IsError, structuredContent = value,
            content = new[] { new { type = "text", text = value.GetRawText() } } };
    }

    private static async Task Send(IHubSocket socket, object value, CancellationToken token)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, Wire);
        if (bytes.Length > MaxBytes) throw new McpFault(McpErrors.InvalidRequest);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(5));
        await socket.SendAsync(bytes.AsMemory(), timeout.Token).ConfigureAwait(false);
    }

    private static async Task<JsonDocument> Receive(IHubSocket socket, CancellationToken token)
    {
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[16384];
        using var frame = CancellationTokenSource.CreateLinkedTokenSource(token);
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk.AsMemory(), frame.Token).ConfigureAwait(false);
            if (result.MessageType != WebSocketMessageType.Text || buffer.Length + result.Count > MaxBytes)
                throw new McpFault(McpErrors.TransportUnavailable);
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) return JsonDocument.Parse(buffer.ToArray(), new() { MaxDepth = 64 });
            frame.CancelAfter(TimeSpan.FromSeconds(10));
        }
    }
}

internal interface IHubSocket : IDisposable
{
    Task ConnectAsync(Uri endpoint, string credential, CancellationToken token);
    ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token);
    ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> bytes, CancellationToken token);
    void Abort();
}
internal sealed class HubSocket : IHubSocket
{
    private readonly ClientWebSocket socket = new();
    public Task ConnectAsync(Uri endpoint, string credential, CancellationToken token)
    {
        socket.Options.SetRequestHeader("Authorization", "Bearer " + credential);
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
        socket.Options.KeepAliveTimeout = TimeSpan.FromSeconds(15);
        return socket.ConnectAsync(endpoint, token);
    }
    public ValueTask SendAsync(ReadOnlyMemory<byte> bytes, CancellationToken token) =>
        socket.SendAsync(bytes, WebSocketMessageType.Text, true, token);
    public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> bytes, CancellationToken token) =>
        socket.ReceiveAsync(bytes, token);
    public void Abort() => socket.Abort();
    public void Dispose() => socket.Dispose();
}
