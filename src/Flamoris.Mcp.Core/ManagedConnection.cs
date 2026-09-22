namespace Flamoris.Mcp.Core;

/// <summary>The provider process state. MCP grant and client attachment are separate axes.</summary>
public enum ManagedConnectionProviderState
{
    Stopped,
    Starting,
    Running,
    Refreshing,
    Stopping,
    Faulted,
}

/// <summary>A UI-neutral view of managed connection state.</summary>
public sealed record ManagedConnectionStatus(
    bool McpEnabled,
    ManagedConnectionProviderState ProviderState,
    bool ExternalClientConnected,
    string? ErrorCode = null)
{
    public bool ProviderRunning => ProviderState == ManagedConnectionProviderState.Running;
}

/// <summary>
/// Host-owned provider implementation. Provider settings, credentials and persistence do not
/// belong to Core; each call must read the host's latest reviewed connection material.
/// </summary>
public interface IMcpConnectionProvider
{
    string Id { get; }
    ValueTask StartAsync(CancellationToken cancellationToken);
    ValueTask RefreshAsync(CancellationToken cancellationToken);
    ValueTask StopAsync(CancellationToken cancellationToken);
}

public static class ManagedConnectionErrors
{
    public const string McpDisabled = "mcp_disabled";
    public const string ProviderStartFailed = "provider_start_failed";
    public const string ProviderRefreshFailed = "provider_refresh_failed";
    public const string ProviderStopFailed = "provider_stop_failed";
    public const string RevokeFailed = "revoke_failed";
}

/// <summary>An intentionally bounded provider error that never retains an unsafe inner exception.</summary>
public sealed class ManagedConnectionException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

/// <summary>
/// Serializes a provider-neutral managed connection lifecycle. The host remains responsible for
/// issuing grants, selecting providers and storing provider settings. MarkEnabledAsync is called only
/// after a current grant exists. Disable and shutdown revoke that grant before stopping the helper.
/// </summary>
public sealed class ManagedConnectionLifecycle : IAsyncDisposable
{
    private readonly IMcpConnectionProvider provider;
    private readonly Func<CancellationToken, ValueTask> revokeMcp;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly object stateGate = new();
    private ManagedConnectionStatus status = new(false, ManagedConnectionProviderState.Stopped, false);
    private bool disposed;

    public ManagedConnectionLifecycle(IMcpConnectionProvider provider,
        Func<CancellationToken, ValueTask> revokeMcp)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(revokeMcp);
        if (provider.Id is not { Length: >= 1 and <= 100 }
            || !provider.Id.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-'))
            throw new ArgumentException("Invalid connection provider identifier.", nameof(provider));
        this.provider = provider;
        this.revokeMcp = revokeMcp;
    }

    public ManagedConnectionStatus Current
    {
        get { lock (stateGate) return status; }
    }

    /// <summary>A notification to reread <see cref="Current"/>; observers cannot break lifecycle work.</summary>
    public event Action? Changed;

    /// <summary>Record that the host has issued a current MCP grant. This does not start a provider.</summary>
    public async Task MarkEnabledAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            lock (stateGate)
            {
                ObjectDisposedException.ThrowIf(disposed, this);
                status = status with
                {
                    McpEnabled = true,
                    ProviderState = status.ProviderState == ManagedConnectionProviderState.Faulted
                        ? ManagedConnectionProviderState.Stopped
                        : status.ProviderState,
                    ErrorCode = null,
                };
            }
            Notify();
        }
        finally { operations.Release(); }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ManagedConnectionStatus current = Current;
            if (!current.McpEnabled) throw new ManagedConnectionException(ManagedConnectionErrors.McpDisabled);
            if (current.ProviderState == ManagedConnectionProviderState.Running) return;

            Transition(current with
            {
                ProviderState = ManagedConnectionProviderState.Starting,
                ErrorCode = null,
            });
            try
            {
                await provider.StartAsync(cancellationToken);
                Transition(Current with { ProviderState = ManagedConnectionProviderState.Running });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupProviderAsync();
                Transition(Current with
                {
                    ProviderState = ManagedConnectionProviderState.Stopped,
                });
                throw;
            }
            catch
            {
                await CleanupProviderAsync();
                Fail(ManagedConnectionErrors.ProviderStartFailed);
                throw new ManagedConnectionException(ManagedConnectionErrors.ProviderStartFailed);
            }
        }
        finally { operations.Release(); }
    }

    /// <summary>Restart or reconfigure the running provider using the host's latest material.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            ManagedConnectionStatus current = Current;
            if (!current.McpEnabled) throw new ManagedConnectionException(ManagedConnectionErrors.McpDisabled);

            bool wasRunning = current.ProviderState == ManagedConnectionProviderState.Running;
            Transition(current with
            {
                ProviderState = wasRunning
                    ? ManagedConnectionProviderState.Refreshing
                    : ManagedConnectionProviderState.Starting,
                ErrorCode = null,
            });
            try
            {
                if (wasRunning) await provider.RefreshAsync(cancellationToken);
                else await provider.StartAsync(cancellationToken);
                Transition(Current with { ProviderState = ManagedConnectionProviderState.Running });
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await CleanupProviderAsync();
                Transition(Current with
                {
                    ProviderState = ManagedConnectionProviderState.Stopped,
                });
                throw;
            }
            catch
            {
                await CleanupProviderAsync();
                Fail(ManagedConnectionErrors.ProviderRefreshFailed);
                throw new ManagedConnectionException(ManagedConnectionErrors.ProviderRefreshFailed);
            }
        }
        finally { operations.Release(); }
    }

    /// <summary>Stop the managed helper but preserve the current MCP grant for manual connection.</summary>
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await StopProviderCoreAsync();
        }
        finally { operations.Release(); }
    }

    /// <summary>Revoke MCP first, then stop the managed helper. Cleanup is not cancellable once begun.</summary>
    public async Task DisableAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            await DisableCoreAsync();
        }
        finally { operations.Release(); }
    }

    /// <summary>Project authenticated external-client state without conflating it with helper state.</summary>
    public void SetExternalClientConnected(bool connected)
    {
        lock (stateGate)
        {
            if (disposed) return;
            if (status.ExternalClientConnected == connected) return;
            status = status with { ExternalClientConnected = connected };
        }
        Notify();
    }

    public async Task ShutdownAsync(CancellationToken cancellationToken = default)
    {
        await operations.WaitAsync(cancellationToken);
        try
        {
            lock (stateGate) { if (disposed) return; }
            try { await DisableCoreAsync(); }
            finally { lock (stateGate) disposed = true; }
        }
        finally { operations.Release(); }
    }

    private async Task DisableCoreAsync()
    {
        Exception? revokeFailure = null;
        if (Current.McpEnabled)
        {
            try { await revokeMcp(CancellationToken.None); }
            catch (Exception exception) { revokeFailure = exception; }
        }

        if (revokeFailure is null)
            Transition(Current with { McpEnabled = false, ExternalClientConnected = false });

        Exception? stopFailure = null;
        try { await StopProviderCoreAsync(); }
        catch (ManagedConnectionException exception) { stopFailure = exception; }

        if (revokeFailure is not null)
        {
            Fail(ManagedConnectionErrors.RevokeFailed);
            throw new ManagedConnectionException(ManagedConnectionErrors.RevokeFailed);
        }
        if (stopFailure is not null) throw stopFailure;
    }

    private async Task StopProviderCoreAsync()
    {
        ManagedConnectionStatus current = Current;
        if (current.ProviderState == ManagedConnectionProviderState.Stopped)
            return;

        Transition(current with
        {
            ProviderState = ManagedConnectionProviderState.Stopping,
        });
        try
        {
            await provider.StopAsync(CancellationToken.None);
            Transition(Current with
            {
                ProviderState = ManagedConnectionProviderState.Stopped,
                ErrorCode = null,
            });
        }
        catch
        {
            Fail(ManagedConnectionErrors.ProviderStopFailed);
            throw new ManagedConnectionException(ManagedConnectionErrors.ProviderStopFailed);
        }
    }

    private async Task CleanupProviderAsync()
    {
        try { await provider.StopAsync(CancellationToken.None); }
        catch { /* the bounded lifecycle error remains the only caller-visible detail */ }
    }

    private void Fail(string errorCode) => Transition(Current with
    {
        ProviderState = ManagedConnectionProviderState.Faulted,
        ErrorCode = errorCode,
    });

    private void Transition(ManagedConnectionStatus next)
    {
        lock (stateGate) status = next;
        Notify();
    }

    private void ThrowIfDisposed()
    {
        lock (stateGate) ObjectDisposedException.ThrowIf(disposed, this);
    }

    private void Notify()
    {
        foreach (Action handler in Changed?.GetInvocationList() ?? [])
            try { handler(); } catch { /* observers cannot poison lifecycle work */ }
    }

    public async ValueTask DisposeAsync()
    {
        try { await ShutdownAsync(); }
        catch (ManagedConnectionException) { }
    }
}
