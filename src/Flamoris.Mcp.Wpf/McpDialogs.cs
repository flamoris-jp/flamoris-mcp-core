using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Flamoris.Mcp.Core;

namespace Flamoris.Mcp.Wpf;

internal static class McpDialogs
{
    private static Window Dialog(Window owner, string title, UIElement content) => new()
    {
        Owner = owner, Title = title, Content = content, Width = 520, SizeToContent = SizeToContent.Height,
        MaxHeight = Math.Max(300, SystemParameters.WorkArea.Height - 80),
        WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize,
    };
    internal static McpPermission? Confirm(Window owner, McpText text, DesktopConnectionSettings settings)
    {
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = text.Confirm(text.Provider(settings.Method)), TextWrapping = TextWrapping.Wrap });
        var permission = Permission(text, settings.Permission);
        AutomationProperties.SetAutomationId(permission, "McpPermission");
        panel.Children.Add(permission);
        var window = Dialog(owner, "MCP / AI", panel);
        Buttons(panel, window, text.Choose("接続", "Connect"), text.Cancel, () => window.DialogResult = true);
        return window.ShowDialog() == true ? (McpPermission)permission.SelectedIndex : null;
    }
    private static ComboBox Permission(McpText text, McpPermission current) => new()
    {
        ItemsSource = new[] { text.ReadOnly, text.Edit }, SelectedIndex = (int)current, Margin = new(0, 8, 0, 8),
    };
    private static void Buttons(Panel panel, Window window, string accept, string cancel, Action save)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new(0, 12, 0, 0) };
        var ok = new Button { Content = accept, MinWidth = 90, Padding = new(8, 4, 8, 4), IsDefault = true };
        AutomationProperties.SetAutomationId(ok, "McpAccept");
        ok.Click += (_, _) => save();
        row.Children.Add(ok);
        row.Children.Add(new Button { Content = cancel, MinWidth = 90, Margin = new(8, 0, 0, 0), Padding = new(8, 4, 8, 4), IsCancel = true });
        panel.Children.Add(row);
    }
    internal static void Settings(Window owner, McpText text, DesktopConnectionSettings current,
        string bridge, Func<DesktopConnectionMethod, IProviderCredentialStore> credentials,
        Func<string?> manual, Func<string?> hubCatalog, Action<DesktopConnectionSettings, bool> save)
    {
        var panel = new StackPanel { Margin = new(20) };
        panel.Children.Add(new TextBlock { Text = text.Method });
        var methods = Enum.GetValues<DesktopConnectionMethod>();
        var method = new ComboBox { ItemsSource = methods.Select(text.Provider), SelectedIndex = (int)current.Method, Margin = new(0, 6, 0, 12) };
        panel.Children.Add(method);
        panel.Children.Add(new TextBlock { Text = text.Permission });
        var permission = Permission(text, current.Permission); panel.Children.Add(permission);
        var auto = new CheckBox { Content = text.Choose("起動時に自動接続", "Connect automatically at startup"), IsChecked = current.AutoConnect, Margin = new(0, 4, 0, 12) };
        panel.Children.Add(auto);
        var manualPanel = new StackPanel();
        var copy = new Button { Content = text.Choose("接続設定をコピー", "Copy connection settings"), HorizontalAlignment = HorizontalAlignment.Left, Padding = new(8, 4, 8, 4), IsEnabled = manual() is not null };
        copy.Click += (_, _) => { try { if (manual() is { } value) Clipboard.SetText(value); } catch { MessageBox.Show(owner, text.Choose("クリップボードを使用できません。", "Clipboard unavailable."), "MCP / AI"); } };
        AutomationProperties.SetAutomationId(copy, "McpCopySettings");
        manualPanel.Children.Add(copy);
        var configuration = new TextBox { Text = manual() ?? "", IsReadOnly = true, TextWrapping = TextWrapping.Wrap,
            MaxHeight = 96, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new(0, 8, 0, 0) };
        AutomationProperties.SetAutomationId(configuration, "McpConnection");
        manualPanel.Children.Add(configuration);
        manualPanel.Children.Add(new TextBlock { Text = text.Choose("接続を開始すると、一時的な接続設定をコピーできます。", "Start a connection to copy its temporary settings."), TextWrapping = TextWrapping.Wrap, Margin = new(0, 6, 0, 6) });
        panel.Children.Add(manualPanel);
        var tunnel = new StackPanel(); panel.Children.Add(tunnel);
        TextBox Field(Panel target, string label, string value)
        {
            target.Children.Add(new TextBlock { Text = label, Margin = new(0, 5, 0, 3) });
            var box = new TextBox { Text = value, MaxLength = 2048, Padding = new(4) }; target.Children.Add(box); return box;
        }
        var executable = Field(tunnel, "tunnel-client.exe", current.TunnelClientExecutable);
        var directory = Field(tunnel, text.Choose("プロファイルフォルダー", "Profile directory"), current.ProfileDirectory);
        var profile = Field(tunnel, text.Choose("プロファイル名", "Profile name"), current.ProfileName);
        var tunnelId = Field(tunnel, "Tunnel ID", current.TunnelId);
        var plane = Field(tunnel, text.Choose("接続先URL", "Control plane URL"), current.ControlPlaneBaseUrl);
        var health = Field(tunnel, text.Choose("ローカル確認用アドレス", "Local health address"), current.HealthListenAddress);
        var hub = new StackPanel(); panel.Children.Add(hub);
        var endpoint = Field(hub, text.Choose("Hub接続先（wss://…/desktop/…）", "Hub endpoint (wss://…/desktop/…)"), current.HubEndpoint);
        var catalog = new Button { Content = text.Choose("Hub登録設定をコピー", "Copy Hub registration settings"), Margin = new(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        catalog.Click += (_, _) =>
        {
            try { if (hubCatalog() is { } value) Clipboard.SetText(value); }
            catch { errorMessage(); }
        };
        void errorMessage() => MessageBox.Show(owner, text.Choose("接続を開始してからコピーしてください。", "Start the connection before copying."), "MCP / AI");
        hub.Children.Add(catalog);
        var secretPanel = new StackPanel { Margin = new(0, 12, 0, 0) }; panel.Children.Add(secretPanel);
        var secretState = new TextBlock(); secretPanel.Children.Add(secretState);
        var password = new PasswordBox { MaxLength = 1024, Padding = new(4), Margin = new(0, 6, 0, 6) }; secretPanel.Children.Add(password);
        var delete = new CheckBox { Content = text.Choose("保存済みの認証情報を削除", "Remove saved credential") }; secretPanel.Children.Add(delete);
        void Refresh()
        {
            var selected = methods[method.SelectedIndex];
            manualPanel.Visibility = selected == DesktopConnectionMethod.Manual ? Visibility.Visible : Visibility.Collapsed;
            tunnel.Visibility = selected == DesktopConnectionMethod.OpenAiTunnelClient ? Visibility.Visible : Visibility.Collapsed;
            hub.Visibility = selected == DesktopConnectionMethod.Hub ? Visibility.Visible : Visibility.Collapsed;
            secretPanel.Visibility = selected == DesktopConnectionMethod.Manual ? Visibility.Collapsed : Visibility.Visible;
            bool exists = false; try { exists = credentials(selected).Exists(); } catch { }
            secretState.Text = exists ? text.Choose("認証情報：保存済み（空欄で保持）", "Credential saved (leave blank to keep)") : text.Choose("認証情報：未設定", "Credential not set");
            password.Clear(); delete.IsChecked = false;
        }
        method.SelectionChanged += (_, _) => Refresh(); Refresh();
        var error = new TextBlock { TextWrapping = TextWrapping.Wrap, Foreground = System.Windows.Media.Brushes.Firebrick, Margin = new(0, 8, 0, 0) }; panel.Children.Add(error);
        var window = Dialog(owner, text.Choose("MCP設定", "MCP settings"), new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Buttons(panel, window, text.Choose("保存", "Save"), text.Cancel, () =>
        {
            var next = current with { Method = methods[method.SelectedIndex], Permission = (McpPermission)permission.SelectedIndex,
                AutoConnect = auto.IsChecked == true, TunnelClientExecutable = executable.Text.Trim(), ProfileDirectory = directory.Text.Trim(),
                ProfileName = profile.Text.Trim(), TunnelId = tunnelId.Text.Trim(), ControlPlaneBaseUrl = plane.Text.Trim(),
                HealthListenAddress = health.Text.Trim(), HubEndpoint = endpoint.Text.Trim() };
            try
            {
                next.Validate(bridge);
                bool secretChanged = next.Method != DesktopConnectionMethod.Manual && (delete.IsChecked == true || password.Password.Length > 0);
                if (next.Method != DesktopConnectionMethod.Manual)
                {
                    if (delete.IsChecked == true) credentials(next.Method).Delete();
                    else if (password.Password.Length > 0) credentials(next.Method).Save(password.Password);
                }
                save(next, secretChanged); password.Clear(); window.DialogResult = true;
            }
            catch { error.Text = text.Choose("保存できません。入力内容と認証情報を確認してください。", "Unable to save. Check the fields and credential."); }
        });
        window.Closed += (_, _) => { password.Clear(); configuration.Clear(); };
        window.ShowDialog();
    }
}
