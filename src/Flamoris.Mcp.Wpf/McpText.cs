using System.Globalization;
namespace Flamoris.Mcp.Wpf;

internal sealed class McpText(Func<CultureInfo> culture)
{
    public string Choose(string ja, string en) => culture().TwoLetterISOLanguageName == "en" ? en : ja;
    public string Connect => Choose("接続...", "Connect...");
    public string Stop => Choose("停止", "Stop");
    public string Settings => Choose("設定...", "Settings...");
    public string Permission => Choose("権限", "Permission");
    public string ReadOnly => Choose("読み取り専用", "Read only");
    public string Edit => Choose("編集を許可", "Allow editing");
    public string Connected => Choose("MCP: 接続中", "MCP: Connected");
    public string Disconnected => Choose("MCP: 未接続", "MCP: Disconnected");
    public string Method => Choose("接続方法", "Connection method");
    public string Cancel => Choose("キャンセル", "Cancel");
    public string Failure => Choose("MCPに接続できません。設定と接続先を確認してください。", "MCP could not connect. Check settings and the connection endpoint.");
    public string PermissionChanged => Choose("権限が変更されました。再接続しますか？", "Permission changed. Reconnect now?");
    public string SettingsChanged => Choose("接続設定が変更されました。再接続しますか？", "Connection settings changed. Reconnect now?");
    public string Confirm(string method) => Choose($"{method} と接続します。よろしいですか？", $"Connect to {method}?");
    public string Provider(Core.DesktopConnectionMethod method) => method switch {
        Core.DesktopConnectionMethod.OpenAiTunnelClient => "OpenAI tunnel-client",
        Core.DesktopConnectionMethod.Hub => "FLAMORIS MCP Hub", _ => "Manual" };
}
