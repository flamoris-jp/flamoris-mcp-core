using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flamoris.Mcp.Core;

public enum McpPermission { ReadOnly, Edit }
public enum OperationKind { Query, Mutation, Undo, Redo, Transaction }

// Values come from the live authority. Core never increments Revision.
public sealed record HostSnapshot(string ProductId, string ApplicationVersion, string RuntimeId,
    string DocumentToken, long Revision, bool Available = true, bool HumanEditing = false);

public interface IMcpHost
{
    // Accessed ONLY on the host's serialization lane.
    HostSnapshot Snapshot { get; }
    // Serialize with human commands, transactions, Undo/Redo and replacement.
    // Cancellation must prevent a queued callback from being invoked.
    Task<T> InvokeAsync<T>(Func<T> action, CancellationToken cancellationToken);
    // Fire BEFORE document/session replacement or shutdown. Failed open need not fire.
    event Action? Invalidating;
}

public sealed record RequestGuard(string RuntimeId, string DocumentToken, long? ExpectedRevision);

public sealed record McpOptions
{
    public bool Enabled { get; init; }
    public McpPermission Permission { get; init; } = McpPermission.ReadOnly;
    public string Transport { get; init; } = "stdioBridge";
    public string PipeName { get; init; } = "flamoris-" + Guid.NewGuid().ToString("N");
    public int RequestTimeoutMs { get; init; } = 15_000;
    public int MaxRequestBytes { get; init; } = 4 * 1024 * 1024;
    public int MaxConcurrentRequests { get; init; } = 4;
    public bool ShowConnectionStatus { get; init; } = true;
    public bool ShowActivityCursor { get; init; } = true;
    public int ReadTimeoutMs { get; init; } = 120_000;
    public int WriteTimeoutMs { get; init; } = 5_000;
    public void Validate()
    {
        if (Transport != "stdioBridge" || !ValidPipeName(PipeName)
            || RequestTimeoutMs is < 10 or > 120_000 || MaxRequestBytes is < 1024 or > 16 * 1024 * 1024
            || MaxConcurrentRequests is < 1 or > 32 || ReadTimeoutMs is < 100 or > 300_000
            || WriteTimeoutMs is < 100 or > 30_000 || !Enum.IsDefined(Permission))
            throw new ArgumentException("Invalid MCP configuration.");
    }
    public static bool ValidPipeName(string name) =>
        name is { Length: >= 8 and <= 100 } && name.StartsWith("flamoris-", StringComparison.Ordinal)
        && name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-');
}

public static class McpErrors
{
    public const string Unauthorized = "unauthorized", Forbidden = "forbidden",
        HostUnavailable = "host_unavailable", TransportUnavailable = "transport_unavailable",
        StaleSession = "stale_session", StaleDocument = "stale_document",
        StaleRevision = "stale_revision", Busy = "busy", Cancelled = "cancelled",
        Timeout = "timeout", InvalidRequest = "invalid_request",
        UnsupportedCapability = "unsupported_capability", InternalError = "internal_error";
}

public sealed class McpFault(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public sealed record McpResult(JsonElement? Value = null, string? Error = null)
{
    public bool IsError => Error is not null;
    public static McpResult Failure(string code) => new(Error: code);
}

// Each tool has a host-owned closed DTO decoder/schema, not a generic mutation endpoint.
public abstract class HostTool
{
    protected HostTool(string name, string description, JsonElement inputSchema,
        OperationKind kind, bool foreground)
    {
        if (name.Length is < 1 or > 100 || !name.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            || description.Length > 2048 || inputSchema.ValueKind != JsonValueKind.Object
            || !Enum.IsDefined(kind))
            throw new ArgumentException("Invalid tool definition.");
        Name = name; Description = description; InputSchema = inputSchema.Clone();
        Kind = kind; Foreground = foreground;
    }
    public string Name { get; }
    public string Description { get; }
    public JsonElement InputSchema { get; }
    public OperationKind Kind { get; }
    public bool Foreground { get; }
    internal abstract Task<JsonElement> ExecuteAsync(RequestContext context, JsonElement input, CancellationToken token);
}

public sealed class HostTool<T> : HostTool
{
    private readonly Func<JsonElement, T> decode;
    private readonly Func<RequestContext, T, CancellationToken, Task<JsonElement>> execute;
    public HostTool(string name, string description, JsonElement inputSchema, OperationKind kind,
        Func<JsonElement, T> decode, Func<RequestContext, T, CancellationToken, Task<JsonElement>> execute,
        bool foreground = true) : base(name, description, inputSchema, kind, foreground)
    { this.decode = decode; this.execute = execute; }
    internal override Task<JsonElement> ExecuteAsync(RequestContext context, JsonElement input, CancellationToken token)
    {
        T value;
        try { value = decode(input); }
        catch (Exception e) when (e is JsonException or ArgumentException or InvalidOperationException or FormatException)
        { throw new McpFault(McpErrors.InvalidRequest); }
        return execute(context, value, token);
    }
}
