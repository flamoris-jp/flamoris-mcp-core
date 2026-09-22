using System.Diagnostics;

namespace Flamoris.Mcp.Samples.ManagedConnection;

public sealed class TunnelClientSampleException(string code) : Exception(code)
{
    public string Code { get; } = code;
}

public interface IOwnedProcess : IDisposable
{
    bool HasExited { get; }
    void Kill(bool entireProcessTree);
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IOwnedProcessLauncher
{
    IOwnedProcess Start(ProcessStartInfo startInfo);
}

public sealed class SystemOwnedProcessLauncher : IOwnedProcessLauncher
{
    public IOwnedProcess Start(ProcessStartInfo startInfo) =>
        new SystemOwnedProcess(Process.Start(startInfo)
            ?? throw new TunnelClientSampleException("tunnel_client_start_failed"));

    private sealed class SystemOwnedProcess(Process process) : IOwnedProcess
    {
        public bool HasExited => process.HasExited;
        public void Kill(bool entireProcessTree) => process.Kill(entireProcessTree);
        public Task WaitForExitAsync(CancellationToken cancellationToken) =>
            process.WaitForExitAsync(cancellationToken);
        public void Dispose() => process.Dispose();
    }
}

/// <summary>Starts and stops only the exact child process instance owned by this host.</summary>
public sealed class TunnelClientProcessHost(IOwnedProcessLauncher launcher) : IAsyncDisposable
{
    private readonly object gate = new();
    private IOwnedProcess? owned;

    public async ValueTask StartAsync(ManagedConnectionSettings settings,
        ManagedConnectionMaterial material, string apiKey, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(material);
        settings.Validate();
        material.Validate();
        if (string.IsNullOrWhiteSpace(apiKey) || apiKey.IndexOfAny(['\r', '\n', '\0']) >= 0)
            throw new TunnelClientSampleException("control_plane_credential_invalid");

        lock (gate)
            if (owned is { HasExited: false })
                throw new TunnelClientSampleException("tunnel_client_already_running");

        Directory.CreateDirectory(settings.ProfileDirectory);
        string yaml = TunnelClientConfiguration.BuildYaml(settings, material.PipeName);
        string temporary = settings.ProfilePath + ".tmp";
        await File.WriteAllTextAsync(temporary, yaml, cancellationToken);
        File.Move(temporary, settings.ProfilePath, overwrite: true);

        var start = new ProcessStartInfo
        {
            FileName = settings.TunnelClientExecutable,
            WorkingDirectory = Path.GetDirectoryName(settings.TunnelClientExecutable)!,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("run");
        start.ArgumentList.Add("--profile");
        start.ArgumentList.Add(settings.ProfileName);
        start.Environment[TunnelClientConfiguration.ApiKeyEnvironmentVariable] = apiKey;
        start.Environment[TunnelClientConfiguration.CapabilityEnvironmentVariable] = material.Capability;

        IOwnedProcess child;
        try { child = launcher.Start(start); }
        catch (TunnelClientSampleException) { throw; }
        catch { throw new TunnelClientSampleException("tunnel_client_start_failed"); }
        finally
        {
            start.Environment.Remove(TunnelClientConfiguration.ApiKeyEnvironmentVariable);
            start.Environment.Remove(TunnelClientConfiguration.CapabilityEnvironmentVariable);
        }

        if (child.HasExited)
        {
            child.Dispose();
            throw new TunnelClientSampleException("tunnel_client_start_failed");
        }

        lock (gate)
        {
            if (owned is { HasExited: false })
            {
                try { child.Kill(entireProcessTree: true); } catch { }
                child.Dispose();
                throw new TunnelClientSampleException("tunnel_client_already_running");
            }
            owned?.Dispose();
            owned = child;
        }
    }

    public async ValueTask StopAsync(CancellationToken cancellationToken)
    {
        IOwnedProcess? child;
        lock (gate)
        {
            child = owned;
            owned = null;
        }
        if (child is null) return;

        try
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            await child.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch { throw new TunnelClientSampleException("tunnel_client_stop_failed"); }
        finally { child.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        try { await StopAsync(CancellationToken.None); }
        catch (TunnelClientSampleException) { }
    }
}
