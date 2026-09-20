namespace Flamoris.Mcp.Core;

public sealed record McpStatus(bool Enabled, bool EndpointAvailable, bool Connected,
    int ForegroundCount, string? LastError)
{
    public bool IsGreen => Enabled && EndpointAvailable && LastError is null;
    public bool ActivityVisible => ForegroundCount > 0;
}

public sealed class StatusProjection
{
    private readonly object gate = new();
    private readonly HashSet<long> activities = [];
    private long nextId;
    private McpStatus value = new(false, false, false, 0, null);
    public McpStatus Current { get { lock (gate) return value; } }
    // A notification to reread Current, not a possibly stale snapshot. Marshal to UI.
    public event Action? Changed;
    internal void Connection(bool enabled, bool available, bool connected, string? error = null)
    {
        lock (gate)
        {
            if (!enabled || !available) activities.Clear();
            value = new(enabled, available, connected, activities.Count, error);
        }
        Notify();
    }
    public IDisposable BeginForeground()
    {
        long id;
        lock (gate) { id = ++nextId; activities.Add(id); value = value with { ForegroundCount = activities.Count }; }
        Notify();
        return new Activity(this, id);
    }
    private void End(long id)
    {
        lock (gate) { activities.Remove(id); value = value with { ForegroundCount = activities.Count }; }
        Notify();
    }
    private void Notify()
    {
        foreach (Action handler in Changed?.GetInvocationList() ?? [])
            try { handler(); } catch { /* projection observers cannot poison requests */ }
    }
    private sealed class Activity(StatusProjection owner, long id) : IDisposable
    {
        private int ended;
        public void Dispose() { if (Interlocked.Exchange(ref ended, 1) == 0) owner.End(id); }
    }
}
