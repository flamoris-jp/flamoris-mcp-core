using System.Text;
using System.Text.Json;

namespace Flamoris.Mcp.Samples.ManagedConnection;

/// <summary>Exact configuration shape verified for tunnel-client v0.0.14.</summary>
public static class TunnelClientConfiguration
{
    public const string ApiKeyEnvironmentVariable = "CONTROL_PLANE_API_KEY";
    public const string CapabilityEnvironmentVariable = "FLAMORIS_MCP_CAPABILITY";
    public const string SupportedVersion = "0.0.14";

    public static string BuildYaml(ManagedConnectionSettings settings, string pipeName)
    {
        ArgumentNullException.ThrowIfNull(settings);
        settings.Validate();
        if (!Flamoris.Mcp.Core.McpOptions.ValidPipeName(pipeName))
            throw new ArgumentException("Invalid local pipe name.", nameof(pipeName));

        string command = WindowsCommandLine.Quote(settings.BridgeExecutable)
            + " --pipe " + WindowsCommandLine.Quote(pipeName);
        string baseUrl = settings.ControlPlaneBaseUrl.AbsoluteUri.TrimEnd('/');
        return $$"""
            config_version: 1

            control_plane:
              base_url: {{YamlString(baseUrl)}}

            tunnel_id: {{YamlString(settings.TunnelId)}}
            api_key: {{YamlString("env:" + ApiKeyEnvironmentVariable)}}

            health:
              listen_addr: {{YamlString(settings.HealthListenAddress)}}

            open_browser: false

            log:
              level: info
              format: json

            mcp:
              commands:
                - channel: main
                  command: {{YamlString(command)}}
            """;
    }

    // JSON string encoding is a valid YAML double-quoted scalar and avoids a YAML dependency.
    private static string YamlString(string value) => JsonSerializer.Serialize(value);
}

public static class WindowsCommandLine
{
    /// <summary>Quote one argument using the Windows CommandLineToArgvW escaping rules.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new StringBuilder(value.Length + 2).Append('"');
        int backslashes = 0;
        foreach (char character in value)
        {
            if (character == '\\')
            {
                backslashes++;
                continue;
            }
            if (character == '"')
            {
                result.Append('\\', checked(backslashes * 2 + 1)).Append('"');
                backslashes = 0;
                continue;
            }
            result.Append('\\', backslashes).Append(character);
            backslashes = 0;
        }
        result.Append('\\', checked(backslashes * 2)).Append('"');
        return result.ToString();
    }
}
