using System.Text.Json;
using System.Text.Json.Serialization;
using Flamoris.Mcp.Core.Connections;

namespace Flamoris.Mcp.Core;

public enum DesktopConnectionMethod { Manual, OpenAiTunnelClient, Hub }

/// <summary>Non-secret preferences. Current permission is held by the live grant, not this record.</summary>
public sealed record DesktopConnectionSettings
{
    public DesktopConnectionMethod Method { get; init; }
    public McpPermission Permission { get; init; } = McpPermission.ReadOnly;
    public bool AutoConnect { get; init; }
    public string TunnelClientExecutable { get; init; } = "";
    public string ProfileDirectory { get; init; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "tunnel-client");
    public string ProfileName { get; init; } = "flamoris";
    public string TunnelId { get; init; } = "";
    public string ControlPlaneBaseUrl { get; init; } = "https://api.openai.com";
    public string HealthListenAddress { get; init; } = "127.0.0.1:8080";
    public string HubEndpoint { get; init; } = "";

    public OpenAiTunnelClientSettings TunnelSettings(string bridge) => new()
    {
        TunnelClientExecutable = TunnelClientExecutable, ProfileDirectory = ProfileDirectory,
        ProfileName = ProfileName, TunnelId = TunnelId, BridgeExecutable = bridge,
        ControlPlaneBaseUrl = new Uri(ControlPlaneBaseUrl), HealthListenAddress = HealthListenAddress,
    };
    public void Validate(string bridge)
    {
        if (!Enum.IsDefined(Method) || !Enum.IsDefined(Permission)) throw new ArgumentException("Invalid MCP settings.");
        if (Method == DesktopConnectionMethod.OpenAiTunnelClient) TunnelSettings(bridge).Validate();
        if (Method == DesktopConnectionMethod.Hub) new HubConnectionSettings(new Uri(HubEndpoint)).Validate();
    }
    public bool RequiresReconnect(DesktopConnectionSettings previous) =>
        this with { AutoConnect = previous.AutoConnect } != previous;
}

public sealed class DesktopConnectionSettingsStore(string path, string productId)
{
    private static readonly JsonSerializerOptions Json = new()
    { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
    public DesktopConnectionSettings Load()
    {
        try
        {
            if (!File.Exists(path)) return new() { ProfileName = productId };
            using var input = File.OpenRead(path);
            if (input.Length > 65536) return new() { ProfileName = productId };
            return JsonSerializer.Deserialize<DesktopConnectionSettings>(input, Json) ?? new() { ProfileName = productId };
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        { return new() { ProfileName = productId }; }
    }
    public void Save(DesktopConnectionSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Json));
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}
