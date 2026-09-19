using System.Globalization;
using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Flamoris.Mcp.Core;

internal static class McpProtocol
{
    internal static async Task ServeAsync(Stream pipe, McpBoundary boundary, CapabilityGrant grant, CancellationToken token)
    {
        await using var bounded = new BoundedProtocolStream(pipe, token, boundary.Options);
        await using var transport = new StreamServerTransport(bounded, bounded, grant.Snapshot.ProductId);
        var tools = new List<Tool> { new() {
            Name = "mcp.context", Description = "Current live identity, revision and permission. Background status.",
            InputSchema = JsonSerializer.SerializeToElement(new { type = "object", properties = new {}, additionalProperties = false }),
        }};
        tools.AddRange(boundary.Tools.Values.Where(t => t.Kind == OperationKind.Query || grant.Permission == McpPermission.Edit)
            .OrderBy(t => t.Name, StringComparer.Ordinal).Select(Describe));
        var options = new McpServerOptions {
            ServerInfo = new() { Name = grant.Snapshot.ProductId, Version = grant.Snapshot.ApplicationVersion },
            ScopeRequests = false, InitializationTimeout = TimeSpan.FromSeconds(10),
            Capabilities = new() { Tools = new() },
            Handlers = new() {
                ListToolsHandler = (request, ct) => {
                    grant.Revoked.ThrowIfCancellationRequested(); ct.ThrowIfCancellationRequested();
                    string? cursor = request.Params?.Cursor;
                    int offset = 0;
                    if (cursor is not null && (!int.TryParse(cursor, NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                        || offset < 0 || offset > tools.Count)) throw new ModelContextProtocol.McpException("invalid_request");
                    return ValueTask.FromResult(new ListToolsResult {
                        Tools = tools.Skip(offset).Take(32).ToList(),
                        NextCursor = offset + 32 < tools.Count ? (offset + 32).ToString(CultureInfo.InvariantCulture) : null,
                    });
                },
                CallToolHandler = async (request, ct) => {
                    using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(ct, bounded.Closed, grant.Revoked);
                    lifetime.CancelAfter(boundary.Options.RequestTimeoutMs);
                    try
                    {
                        string name = request.Params?.Name ?? "";
                        var args = JsonSerializer.SerializeToElement(request.Params?.Arguments ?? new Dictionary<string, JsonElement>());
                        if (name == "mcp.context")
                        {
                            if (args.EnumerateObject().Any()) return ToWire(McpResult.Failure(McpErrors.InvalidRequest));
                            return ToWire(new(await boundary.ContextAsync(grant, lifetime.Token).WaitAsync(lifetime.Token)));
                        }
                        if (args.EnumerateObject().Any(p => p.Name is not ("guard" or "input"))
                            || !args.TryGetProperty("input", out var input))
                            return ToWire(McpResult.Failure(McpErrors.InvalidRequest));
                        RequestGuard? guard = args.TryGetProperty("guard", out var g) ? ParseGuard(g) : null;
                        // The boundary owns operation deadlines; outer token only carries disconnect/client cancellation.
                        using var requestToken = CancellationTokenSource.CreateLinkedTokenSource(ct, bounded.Closed, grant.Revoked);
                        return ToWire(await boundary.InvokeAsync(grant, name, input, guard, requestToken.Token));
                    }
                    catch (McpFault e) { return ToWire(McpResult.Failure(e.Code)); }
                    catch (OperationCanceledException) { return ToWire(McpResult.Failure(McpErrors.Cancelled)); }
                    catch (Exception e) when (e is JsonException or InvalidOperationException or FormatException)
                    { return ToWire(McpResult.Failure(McpErrors.InvalidRequest)); }
                    catch { return ToWire(McpResult.Failure(McpErrors.InternalError)); }
                },
            },
        };
        boundary.Diagnostics.Event("mcp.protocol", "started");
        await using var server = McpServer.Create(transport, options);
        try { await server.RunAsync(bounded.Closed); }
        finally { boundary.Diagnostics.Event("mcp.protocol", "stopped"); }
    }
    private static Tool Describe(HostTool tool)
    {
        var properties = new Dictionary<string, object> {
            ["input"] = tool.InputSchema,
            ["guard"] = new {
                type = "object", additionalProperties = false,
                properties = new {
                    runtimeId = new { type = "string", minLength = 1, maxLength = 128 },
                    documentToken = new { type = "string", minLength = 1, maxLength = 128 },
                    expectedRevision = new { type = "string", pattern = "^(0|[1-9][0-9]*)$", maxLength = 19 },
                },
                required = tool.Kind == OperationKind.Query ? new[] { "runtimeId", "documentToken" }
                    : new[] { "runtimeId", "documentToken", "expectedRevision" },
            },
        };
        return new Tool {
            Name = tool.Name, Description = tool.Description,
            InputSchema = JsonSerializer.SerializeToElement(new {
                type = "object", properties, additionalProperties = false,
                required = tool.Kind == OperationKind.Query ? new[] { "input" } : new[] { "input", "guard" },
            }),
        };
    }
    private static RequestGuard ParseGuard(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object || value.EnumerateObject().Any(p =>
            p.Name is not ("runtimeId" or "documentToken" or "expectedRevision")))
            throw new McpFault(McpErrors.InvalidRequest);
        string Text(string name)
        {
            if (!value.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String
                || v.GetString() is not { Length: >= 1 and <= 128 } text) throw new McpFault(McpErrors.InvalidRequest);
            return text;
        }
        long? revision = null;
        if (value.TryGetProperty("expectedRevision", out _))
        {
            string text = Text("expectedRevision");
            if (!long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out long n)
                || n < 0 || n.ToString(CultureInfo.InvariantCulture) != text) throw new McpFault(McpErrors.InvalidRequest);
            revision = n;
        }
        return new(Text("runtimeId"), Text("documentToken"), revision);
    }
    private static CallToolResult ToWire(McpResult result)
    {
        JsonElement value = result.Error is { } code
            ? JsonSerializer.SerializeToElement(new { error = new { code } })
            : result.Value ?? JsonSerializer.SerializeToElement(new {});
        return new() { IsError = result.IsError, StructuredContent = value,
            Content = [new TextContentBlock { Text = value.GetRawText() }] };
    }
}
