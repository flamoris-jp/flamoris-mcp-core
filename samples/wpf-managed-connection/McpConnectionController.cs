using Flamoris.Mcp.Core;

namespace Flamoris.Mcp.Samples.ManagedConnection;

/// <summary>
/// Copyable orchestration example. The WPF host supplies its real grant issue/rotation callbacks,
/// persists only non-secret settings and projects Changed onto its Dispatcher-owned UI.
/// </summary>
public sealed class McpConnectionController(
    ManagedConnectionSettings settings,
    ManagedConnectionLifecycle lifecycle,
    Func<CancellationToken, ValueTask> issueGrant,
    Func<CancellationToken, ValueTask> rotateGrant)
{
    public ManagedConnectionStatus Current => lifecycle.Current;
    public event Action? Changed
    {
        add => lifecycle.Changed += value;
        remove => lifecycle.Changed -= value;
    }

    public async Task EnableAsync(CancellationToken cancellationToken = default)
    {
        await issueGrant(cancellationToken);
        // A successful host mutation must be committed to lifecycle state even if the caller
        // cancels at that boundary; otherwise a later disable could skip revoking a live grant.
        await lifecycle.MarkEnabledAsync(CancellationToken.None);
        if (settings.AutoStart) await lifecycle.StartAsync(CancellationToken.None);
    }

    public Task StartManagedConnectionAsync(CancellationToken cancellationToken = default) =>
        lifecycle.StartAsync(cancellationToken);

    public async Task RefreshAfterGrantRotationAsync(CancellationToken cancellationToken = default)
    {
        await rotateGrant(cancellationToken);
        // Once rotation succeeds, reconcile the owned provider with the new material before
        // observing cancellation so it cannot keep using the superseded grant.
        await lifecycle.RefreshAsync(CancellationToken.None);
    }

    public Task StopManagedConnectionAsync(CancellationToken cancellationToken = default) =>
        lifecycle.StopAsync(cancellationToken);

    public Task DisableAsync(CancellationToken cancellationToken = default) =>
        lifecycle.DisableAsync(cancellationToken);

    public Task ShutdownAsync(CancellationToken cancellationToken = default) =>
        lifecycle.ShutdownAsync(cancellationToken);

    public void ProjectExternalClient(bool connected) =>
        lifecycle.SetExternalClientConnected(connected);
}
