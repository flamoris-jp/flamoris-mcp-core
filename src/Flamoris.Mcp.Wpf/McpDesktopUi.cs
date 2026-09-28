using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Flamoris.Mcp.Core;
using Flamoris.Mcp.Core.Connections;

namespace Flamoris.Mcp.Wpf;

public sealed record McpDesktopAttachment(McpBoundary Boundary, CapabilityGrant Grant, Func<Task>? StopHost = null);

/// <summary>One shared menu, settings dialog, lifecycle and feedback implementation for desktop hosts.</summary>
public sealed class McpDesktopUi
{
    private readonly Window owner;
    private readonly MenuItem menu;
    private readonly Func<McpPermission, Task<McpDesktopAttachment>> attach;
    private readonly Func<bool> ready;
    private readonly string productId, bridge, credentialPrefix;
    private readonly DesktopConnectionSettingsStore store;
    private readonly McpText text;
    private readonly SemaphoreSlim operations = new(1, 1);
    private readonly MenuItem connect = new(), stop = new(), settings = new();
    private readonly Image dot = new() { Width = 20, Height = 20 };
    private readonly Image chipsy = new() { Width = 48, Height = 48, Stretch = Stretch.Uniform,
        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false, Focusable = false, Opacity = 0 };
    private readonly TranslateTransform jump = new();
    private McpDesktopAttachment? attachment;
    private ManagedConnectionLifecycle? lifecycle;
    private TunnelClientProcessHost? process;
    private DesktopConnectionSettings saved, live;
    private bool connected, busy, closed, wanted, autoAttempted, reconnectPending;
    private long generation;
    private string? error;
    public FrameworkElement StatusIndicator => dot;
    public bool IsEnabled => attachment?.Grant.IsActive == true;
    public McpDesktopAttachment? Attachment => attachment;

    public McpDesktopUi(Window owner, MenuItem menu, string productId, string settingsPath,
        string credentialPrefix, string bridge, Func<McpPermission, Task<McpDesktopAttachment>> attach,
        Func<bool> ready, Func<CultureInfo>? culture = null)
    {
        this.owner = owner; this.menu = menu; this.productId = productId; this.bridge = bridge;
        this.credentialPrefix = credentialPrefix; this.attach = attach; this.ready = ready;
        store = new(settingsPath, productId); saved = live = store.Load();
        text = new(culture ?? (() => CultureInfo.CurrentUICulture));
        AutomationProperties.SetAutomationId(connect, "McpConnectMenu");
        AutomationProperties.SetAutomationId(stop, "McpStopMenu");
        AutomationProperties.SetAutomationId(settings, "McpSettingsMenu");
        menu.Items.Clear(); menu.Items.Add(connect); menu.Items.Add(stop); menu.Items.Add(settings);
        connect.Click += async (_, _) =>
        {
            var permission = McpDialogs.Confirm(owner, text, saved);
            if (permission is null) return;
            try { saved = saved with { Permission = permission.Value }; store.Save(saved); }
            catch { error = "settings_unavailable"; Refresh(); return; }
            wanted = true; await ConnectAsync(saved);
        };
        stop.Click += async (_, _) => { wanted = false; autoAttempted = true; await StopAsync(); };
        settings.Click += (_, _) => ShowSettings();
        menu.SubmenuOpened += (_, _) => Refresh();
        chipsy.Source = Asset("chipsy-acknowledgement.png"); chipsy.RenderTransform = jump;
        AutomationProperties.SetName(chipsy, "Chipsy");
        AutomationProperties.SetAutomationId(dot, "McpStatus");
        if (owner.Content is UIElement content)
        {
            owner.Content = null;
            var overlay = new Grid(); overlay.Children.Add(content); overlay.Children.Add(chipsy); owner.Content = overlay;
        }
        owner.Closed += (_, _) => Shutdown();
        owner.Loaded += (_, _) => NotifyHostReady();
        Refresh();
    }
    private static BitmapImage Asset(string name) => new(new Uri("pack://application:,,,/Flamoris.Mcp.Wpf;component/Assets/" + name));
    private WindowsCredentialStore Credential(DesktopConnectionMethod method) => new(credentialPrefix + "/" +
        (method == DesktopConnectionMethod.Hub ? "Hub" : "OpenAiTunnelClient"));

    public void Refresh()
    {
        if (!owner.Dispatcher.CheckAccess()) { Post(Refresh); return; }
        menu.Header = "MCP / AI"; connect.Header = text.Connect; stop.Header = text.Stop; settings.Header = text.Settings;
        connect.IsEnabled = !busy && !closed && ready(); stop.IsEnabled = !busy && (IsEnabled || wanted);
        settings.IsEnabled = !busy && !closed;
        bool now = attachment?.Grant.IsActive == true && attachment.Boundary.Status.Current.IsGreen;
        dot.Source = Asset(now ? "mcp-status-green_20.png" : "mcp-status-red_20.png");
        string label = now ? text.Connected : text.Disconnected;
        var active = IsEnabled ? live : saved;
        dot.ToolTip = label + "\n" + text.Method + ": " + text.Provider(active.Method) + "\n" + text.Permission + ": "
            + (active.Permission == McpPermission.Edit ? text.Edit : text.ReadOnly)
            + (error is not null || attachment?.Boundary.Status.Current.LastError is not null ? "\n" + text.Failure : "");
        AutomationProperties.SetName(dot, label);
        if (now && !connected) Acknowledge(); connected = now;
    }
    private void Post(Action action)
    {
        if (closed || owner.Dispatcher.HasShutdownStarted) return;
        if (owner.Dispatcher.CheckAccess()) action();
        else _ = owner.Dispatcher.BeginInvoke(action);
    }
    private void StatusChanged() => Post(Refresh);
    private void MutationSucceeded() => Post(Acknowledge);
    private void Acknowledge()
    {
        if (closed) return;
        chipsy.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimationUsingKeyFrames {
            Duration = TimeSpan.FromMilliseconds(500), KeyFrames = {
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.Zero)),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(70))),
                new LinearDoubleKeyFrame(1, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(300))),
                new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(500))) } });
        jump.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(8, 0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut } });
    }
    public void NotifyHostReady()
    {
        if (closed || busy || IsEnabled || !ready()) return;
        if (!wanted && (!saved.AutoConnect || autoAttempted)) return;
        autoAttempted = true; wanted = true;
        _ = ConnectAsync(saved);
    }
    // Synchronous revocation is essential before a host replaces its document or exits.
    public void Invalidate()
    {
        generation++; reconnectPending = wanted;
        attachment?.Boundary.Disable(); connected = false;
        _ = StopAsync();
    }
    public void Shutdown()
    {
        if (closed) return;
        closed = true; wanted = false; generation++;
        attachment?.Boundary.Disable();
        // Cleanup does not need the dispatcher. Avoid blocking a WPF shutdown on queued callbacks.
        _ = StopAsync();
    }
    public async Task ConnectAsync(DesktopConnectionSettings preferences)
    {
        await operations.WaitAsync();
        try
        {
            if (closed || !ready()) return;
            busy = true; error = null; Refresh();
            await StopCore();
            long expected = generation;
            preferences.Validate(bridge);
            var next = await attach(preferences.Permission);
            if (closed || expected != generation)
            {
                next.Boundary.Dispose();
                if (next.StopHost is { } stopHost) await stopHost();
                return;
            }
            attachment = next; live = preferences;
            next.Boundary.Status.Changed += StatusChanged;
            next.Boundary.Status.MutationSucceeded += MutationSucceeded;
            IMcpConnectionProvider provider;
            if (live.Method == DesktopConnectionMethod.Hub)
                provider = new HubConnectionProvider(new(new Uri(live.HubEndpoint)), next.Boundary, next.Grant,
                    Credential(live.Method).GetControlPlaneApiKeyAsync);
            else
            {
                _ = Task.Run(() => new LocalMcpEndpoint(next.Boundary).RunAsync(next.Grant));
                if (live.Method == DesktopConnectionMethod.OpenAiTunnelClient)
                {
                    process = new(new SystemOwnedProcessLauncher());
                    process.UnexpectedExit += ProviderExited;
                    provider = new TunnelClientConnectionProvider(live.TunnelSettings(bridge), new Material(next), Credential(live.Method), process);
                }
                else provider = new ManualProvider();
            }
            lifecycle = new(provider, _ => { next.Boundary.Disable(); return ValueTask.CompletedTask; });
            await new McpConnectionController(lifecycle).EnableAsync(_ => Task.CompletedTask, startManagedHelper: true);
        }
        catch
        {
            wanted = false; reconnectPending = false;
            error = "connection_failed";
            await StopCore();
        }
        finally { busy = false; operations.Release(); Refresh(); }
    }
    private void ProviderExited() => Post(() => { wanted = false; error = "connection_failed"; Invalidate(); });
    public async Task StopAsync()
    {
        await operations.WaitAsync();
        try { busy = true; await StopCore(); }
        finally
        {
            busy = false; operations.Release();
            if (!closed) Post(() => { Refresh(); if (reconnectPending)
                {
                    reconnectPending = false;
                    _ = owner.Dispatcher.BeginInvoke(new Action(NotifyHostReady), System.Windows.Threading.DispatcherPriority.Background);
                } });
        }
    }
    private async Task StopCore()
    {
        var old = attachment; attachment = null;
        old?.Boundary.Disable();
        if (old is not null) { old.Boundary.Status.Changed -= StatusChanged; old.Boundary.Status.MutationSucceeded -= MutationSucceeded; }
        var oldLifecycle = lifecycle; lifecycle = null;
        var oldProcess = process; process = null;
        if (oldProcess is not null) oldProcess.UnexpectedExit -= ProviderExited;
        if (oldLifecycle is not null) await oldLifecycle.DisposeAsync().ConfigureAwait(false);
        if (oldProcess is not null) await oldProcess.DisposeAsync().ConfigureAwait(false);
        if (old?.StopHost is { } stopHost) { try { await stopHost().ConfigureAwait(false); } catch { } }
        old?.Boundary.Dispose(); connected = false;
    }
    public string? ManualConnection()
    {
        var value = attachment;
        if (value?.Grant.IsActive != true || live.Method != DesktopConnectionMethod.Manual) return null;
        return JsonSerializer.Serialize(new { mcpServers = new Dictionary<string, object> {
            [productId] = new { command = bridge, args = new[] { "--pipe", value.Boundary.Options.PipeName },
                env = new Dictionary<string, string> { [StdioBridge.CredentialEnvironmentVariable] = value.Grant.ExportCredential() } } } },
            new JsonSerializerOptions { WriteIndented = true });
    }
    private string? HubCatalog()
    {
        var value = attachment;
        if (value?.Grant.IsActive != true) return null;
        string id = new Uri(live.HubEndpoint).Segments.Last().Trim('/');
        if (id.Length == 0 || !id.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_')) return null;
        return JsonSerializer.Serialize(new { id, @namespace = id, transport = "reverse-websocket",
            product_id = value.Grant.Snapshot.ProductId,
            connector_token_env = id.ToUpperInvariant().Replace('-', '_') + "_CONNECTOR_TOKEN",
            tools = value.Boundary.DescribeCatalog() }, new JsonSerializerOptions { WriteIndented = true });
    }
    private void ShowSettings()
    {
        DesktopConnectionSettings? next = null; bool secretChanged = false;
        McpDialogs.Settings(owner, text, saved, bridge, Credential, ManualConnection, HubCatalog, (value, changed) =>
        { store.Save(value); next = value; secretChanged = changed; });
        if (next is null) return;
        saved = next;
        if (IsEnabled && (saved.RequiresReconnect(live) || secretChanged))
        {
            string prompt = saved.Permission != live.Permission ? text.PermissionChanged : text.SettingsChanged;
            if (MessageBox.Show(owner, prompt, "MCP / AI", MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.OK)
                _ = ConnectAsync(saved);
        }
        Refresh();
    }
    private sealed class Material(McpDesktopAttachment value) : IMcpConnectionMaterialSource
    { public McpConnectionMaterial GetCurrent() => new(value.Boundary.Options.PipeName, value.Grant.ExportCredential()); }
    private sealed class ManualProvider : IMcpConnectionProvider
    {
        public string Id => "manual";
        public ValueTask StartAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask RefreshAsync(CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask StopAsync(CancellationToken token) => ValueTask.CompletedTask;
    }
}
