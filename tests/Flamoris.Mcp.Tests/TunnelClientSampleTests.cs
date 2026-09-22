using System.Diagnostics;
using Flamoris.Mcp.Core;
using Flamoris.Mcp.Samples.ManagedConnection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Flamoris.Mcp.Tests;

[TestClass]
public sealed class TunnelClientSampleTests
{
    private const string Capability = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF";

    [TestMethod]
    public void GeneratedYamlMatchesVersion014SchemaAndOmitsSecrets()
    {
        using var fixture = new SettingsFixture();
        ManagedConnectionSettings settings = fixture.Settings(autoStart: true);

        string yaml = TunnelClientConfiguration.BuildYaml(settings, "flamoris-cutwork-test");

        StringAssert.StartsWith(yaml, "config_version: 1\n");
        StringAssert.Contains(yaml, "control_plane:\n  base_url: \"https://api.openai.com\"");
        StringAssert.Contains(yaml, "tunnel_id: \"flamoris-cutwork\"");
        StringAssert.Contains(yaml, "api_key: \"env:CONTROL_PLANE_API_KEY\"");
        StringAssert.Contains(yaml, "health:\n  listen_addr: \"127.0.0.1:8080\"");
        StringAssert.Contains(yaml, "open_browser: false");
        StringAssert.Contains(yaml, "mcp:\n  commands:\n    - channel: main");
        StringAssert.Contains(yaml, "--pipe \\\"flamoris-cutwork-test\\\"");
        Assert.IsFalse(yaml.Contains(Capability, StringComparison.Ordinal));
        Assert.IsFalse(yaml.Contains("CONTROL_PLANE_SECRET", StringComparison.Ordinal));
        Assert.AreEqual("0.0.14", TunnelClientConfiguration.SupportedVersion);
    }

    [TestMethod]
    public void WindowsCommandLineQuotesPathsContainingSpaces()
    {
        Assert.AreEqual("\"C:\\Program Files\\FLAMORIS\\mcp\\Bridge.exe\"",
            WindowsCommandLine.Quote("C:\\Program Files\\FLAMORIS\\mcp\\Bridge.exe"));
        Assert.AreEqual("\"ends-with-slash\\\\\"", WindowsCommandLine.Quote("ends-with-slash\\"));
    }

    [TestMethod]
    public async Task ProcessHostUsesEnvironmentAndStopsOnlyItsOwnedChild()
    {
        using var fixture = new SettingsFixture();
        var launcher = new FakeLauncher();
        var unrelated = new FakeProcess();
        await using var host = new TunnelClientProcessHost(launcher);
        const string apiKey = "CONTROL_PLANE_SECRET";

        await host.StartAsync(fixture.Settings(autoStart: true),
            new("flamoris-cutwork-test", Capability), apiKey, CancellationToken.None);

        Assert.AreEqual(1, launcher.Starts.Count);
        CapturedStart started = launcher.Starts.Single();
        Assert.IsFalse(started.UseShellExecute);
        CollectionAssert.AreEqual(new[] { "run", "--profile", "flamoris-cutwork" }, started.Arguments);
        Assert.AreEqual(apiKey, started.Environment[TunnelClientConfiguration.ApiKeyEnvironmentVariable]);
        Assert.AreEqual(Capability, started.Environment[TunnelClientConfiguration.CapabilityEnvironmentVariable]);
        string yaml = await File.ReadAllTextAsync(fixture.Settings(true).ProfilePath);
        Assert.IsFalse(yaml.Contains(apiKey, StringComparison.Ordinal));
        Assert.IsFalse(yaml.Contains(Capability, StringComparison.Ordinal));

        await host.StopAsync(CancellationToken.None);

        Assert.IsTrue(launcher.Processes.Single().Killed);
        Assert.IsFalse(unrelated.Killed, "The sample must never scan for or stop unrelated processes.");
    }

    [TestMethod]
    public async Task ControllerKeepsManualModeAndRefreshesAfterRotation()
    {
        using var fixture = new SettingsFixture();
        var provider = new CountingProvider();
        int issued = 0, rotated = 0, revoked = 0;
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ =>
        {
            revoked++;
            return ValueTask.CompletedTask;
        });
        var controller = new McpConnectionController(fixture.Settings(autoStart: false), lifecycle,
            _ => { issued++; return ValueTask.CompletedTask; },
            _ => { rotated++; return ValueTask.CompletedTask; });

        await controller.EnableAsync();
        Assert.AreEqual(1, issued);
        Assert.AreEqual(0, provider.Starts);
        Assert.IsTrue(controller.Current.McpEnabled);
        Assert.AreEqual(ManagedConnectionProviderState.Stopped, controller.Current.ProviderState);

        await controller.StartManagedConnectionAsync();
        await controller.RefreshAfterGrantRotationAsync();
        Assert.AreEqual(1, provider.Starts);
        Assert.AreEqual(1, provider.Refreshes);
        Assert.AreEqual(1, rotated);

        await controller.DisableAsync();
        Assert.AreEqual(1, revoked);
        Assert.AreEqual(1, provider.Stops);
    }

    [TestMethod]
    public async Task ControllerAutoStartIsHostPolicyNotCorePolicy()
    {
        using var fixture = new SettingsFixture();
        var provider = new CountingProvider();
        await using var lifecycle = new ManagedConnectionLifecycle(provider, _ => ValueTask.CompletedTask);
        var controller = new McpConnectionController(fixture.Settings(autoStart: true), lifecycle,
            _ => ValueTask.CompletedTask, _ => ValueTask.CompletedTask);

        await controller.EnableAsync();

        Assert.AreEqual(1, provider.Starts);
        Assert.AreEqual(ManagedConnectionProviderState.Running, controller.Current.ProviderState);
    }

    [TestMethod]
    public async Task ProviderRefreshConsumesLatestPipeAndCapability()
    {
        using var fixture = new SettingsFixture();
        var launcher = new FakeLauncher();
        await using var processHost = new TunnelClientProcessHost(launcher);
        var source = new MutableMaterialSource(new("flamoris-cutwork-first", Capability));
        var credentials = new FixedCredentialSource();
        var provider = new TunnelClientConnectionProvider(fixture.Settings(true), source,
            credentials, processHost);

        await provider.StartAsync(CancellationToken.None);
        source.Current = new("flamoris-cutwork-second", new string('A', 64));
        await provider.RefreshAsync(CancellationToken.None);

        Assert.AreEqual(2, launcher.Starts.Count);
        Assert.AreEqual(Capability, launcher.Starts[0].Environment[
            TunnelClientConfiguration.CapabilityEnvironmentVariable]);
        Assert.AreEqual(new string('A', 64), launcher.Starts[1].Environment[
            TunnelClientConfiguration.CapabilityEnvironmentVariable]);
        Assert.IsTrue(launcher.Processes[0].Killed);
        Assert.IsFalse(launcher.Processes[1].Killed);
    }

    private sealed class SettingsFixture : IDisposable
    {
        private readonly string root = Path.Combine(Path.GetTempPath(), "flamoris-mcp-sample-" + Guid.NewGuid().ToString("N"));
        private readonly string tunnelClient;
        private readonly string bridge;

        public SettingsFixture()
        {
            tunnelClient = Path.Combine(root, "Program Files", "tunnel-client.exe");
            bridge = Path.Combine(root, "FLAMORIS Cutwork", "mcp", "Flamoris.Mcp.Bridge.exe");
            Directory.CreateDirectory(Path.GetDirectoryName(tunnelClient)!);
            Directory.CreateDirectory(Path.GetDirectoryName(bridge)!);
            File.WriteAllText(tunnelClient, "fixture");
            File.WriteAllText(bridge, "fixture");
        }

        public ManagedConnectionSettings Settings(bool autoStart) => new()
        {
            AutoStart = autoStart,
            TunnelClientExecutable = tunnelClient,
            ProfileDirectory = Path.Combine(root, "profiles"),
            ProfileName = "flamoris-cutwork",
            TunnelId = "flamoris-cutwork",
            BridgeExecutable = bridge,
        };

        public void Dispose()
        {
            try { Directory.Delete(root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private sealed record CapturedStart(string FileName, bool UseShellExecute,
        string[] Arguments, Dictionary<string, string?> Environment);

    private sealed class FakeLauncher : IOwnedProcessLauncher
    {
        public List<CapturedStart> Starts { get; } = [];
        public List<FakeProcess> Processes { get; } = [];

        public IOwnedProcess Start(ProcessStartInfo startInfo)
        {
            Starts.Add(new(startInfo.FileName, startInfo.UseShellExecute,
                startInfo.ArgumentList.ToArray(), startInfo.Environment.ToDictionary(
                    item => item.Key, item => item.Value, StringComparer.Ordinal)));
            var process = new FakeProcess();
            Processes.Add(process);
            return process;
        }
    }

    private sealed class FakeProcess : IOwnedProcess
    {
        public bool HasExited { get; private set; }
        public bool Killed { get; private set; }
        public void Kill(bool entireProcessTree)
        {
            Assert.IsTrue(entireProcessTree);
            Killed = true;
            HasExited = true;
        }
        public Task WaitForExitAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        public void Dispose() { }
    }

    private sealed class CountingProvider : IMcpConnectionProvider
    {
        public string Id => "counting";
        public int Starts { get; private set; }
        public int Refreshes { get; private set; }
        public int Stops { get; private set; }
        public ValueTask StartAsync(CancellationToken cancellationToken) { Starts++; return ValueTask.CompletedTask; }
        public ValueTask RefreshAsync(CancellationToken cancellationToken) { Refreshes++; return ValueTask.CompletedTask; }
        public ValueTask StopAsync(CancellationToken cancellationToken) { Stops++; return ValueTask.CompletedTask; }
    }

    private sealed class MutableMaterialSource(ManagedConnectionMaterial current) : IConnectionMaterialSource
    {
        public ManagedConnectionMaterial Current { get; set; } = current;
        public ManagedConnectionMaterial GetCurrent() => Current;
    }

    private sealed class FixedCredentialSource : IProviderCredentialSource
    {
        public ValueTask<string> GetControlPlaneApiKeyAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult("CONTROL_PLANE_SECRET");
    }
}
