using System.Text.Json;
using Flamoris.Logging;
using Flamoris.Mcp.Core;

namespace Flamoris.Mcp.Tests;

// Synthetic host-owned document/history. Only tests own this domain state.
internal sealed class HostHarness(string productId = "flamoris.kachinco") : IMcpHost
{
    private readonly object lane = new();
    private readonly Stack<int> undo = new(), redo = new();
    public int Value { get; private set; }
    public int Commits { get; private set; }
    public HostSnapshot Snapshot { get; private set; } = new(productId, "test", Guid.NewGuid().ToString(),
        Guid.NewGuid().ToString(), 0);
    public event Action? Invalidating;
    public Task<T> InvokeAsync<T>(Func<T> action, CancellationToken token)
    { lock (lane) { token.ThrowIfCancellationRequested(); return Task.FromResult(action()); } }
    public void Replace()
    {
        lock (lane) {
            Invalidating?.Invoke();
            Snapshot = Snapshot with { DocumentToken = Guid.NewGuid().ToString(), Revision = Snapshot.Revision + 1 };
        }
    }
    public void Busy(bool busy) { lock (lane) Snapshot = Snapshot with { HumanEditing = busy }; }
    public void Restart()
    { lock (lane) { Invalidating?.Invoke(); Snapshot = Snapshot with { RuntimeId = Guid.NewGuid().ToString() }; } }
    public JsonElement Read() => JsonSerializer.SerializeToElement(new { value = Value, revision = Snapshot.Revision });
    public JsonElement Set(int value)
    {
        lock (lane) {
            undo.Push(Value); redo.Clear(); Value = value; Commits++;
            Snapshot = Snapshot with { Revision = Snapshot.Revision + 1 }; return Read();
        }
    }
    public JsonElement Undo()
    { lock (lane) { redo.Push(Value); Value = undo.Pop(); Snapshot = Snapshot with { Revision = Snapshot.Revision + 1 }; return Read(); } }
    public JsonElement Redo()
    { lock (lane) { undo.Push(Value); Value = redo.Pop(); Snapshot = Snapshot with { Revision = Snapshot.Revision + 1 }; return Read(); } }
    public RequestGuard Guard(long? revision = null) => new(Snapshot.RuntimeId, Snapshot.DocumentToken, revision ?? Snapshot.Revision);
    public static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);
    public static JsonElement Empty => Json(new {});
    public static McpDiagnostics Diagnostics() => new(FlamorisLogger.Create());
    public McpBoundary Boundary(McpOptions? options = null, IEnumerable<HostTool>? extra = null) =>
        new(this, Tools().Concat(extra ?? []), options ?? new(), Diagnostics());
    public IEnumerable<HostTool> Tools()
    {
        yield return new HostTool<int>("value.set", "Set synthetic value through shared command.",
            Json(new { type = "object", properties = new { value = new { type = "integer" } },
                required = new[] { "value" }, additionalProperties = false }), OperationKind.Mutation,
            input => {
                if (input.EnumerateObject().Count() != 1) throw new ArgumentException();
                return input.GetProperty("value").GetInt32();
            }, (ctx, value, _) => ctx.CommitAsync(() => Set(value)));
        yield return new HostTool<JsonElement>("value.read", "Read live value.", Json(new { type = "object" }),
            OperationKind.Query, x => x, (ctx, _, _) => ctx.ReadAsync(Read), foreground: false);
        yield return new HostTool<JsonElement>("history.undo", "Shared Undo.", Json(new { type = "object" }),
            OperationKind.Undo, x => x, (ctx, _, _) => ctx.CommitAsync(Undo));
        yield return new HostTool<JsonElement>("history.redo", "Shared Redo.", Json(new { type = "object" }),
            OperationKind.Redo, x => x, (ctx, _, _) => ctx.CommitAsync(Redo));
    }
}
