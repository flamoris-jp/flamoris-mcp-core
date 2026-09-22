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
        await lifecycle.MarkEnabledAsync(cancellationToken);
        if (settings.AutoStart) await lifecycle.StartAsync(cancellationToken);
    }

    public Task StartManagedConnectionAsync(CancellationToken cancellationToken = default) =>
        lifecycle.StartAsync(cancellationToken);

    public async Task RefreshAfterGrantRotationAsync(CancellationToken cancellationToken = default)
    {
        await rotateGrant(cancellationToken);
        await lifecycle.RefreshAsync(cancellationToken);
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
