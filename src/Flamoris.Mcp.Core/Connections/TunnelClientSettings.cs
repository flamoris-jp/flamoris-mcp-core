using System.Net;
using System.IO;
namespace Flamoris.Mcp.Core.Connections;

public sealed record OpenAiTunnelClientSettings
{
    public required string TunnelClientExecutable { get; init; }
    public required string ProfileDirectory { get; init; }
    public required string ProfileName { get; init; }
    public required string TunnelId { get; init; }
    public required string BridgeExecutable { get; init; }
    public Uri ControlPlaneBaseUrl { get; init; } = new("https://api.openai.com");
    public string HealthListenAddress { get; init; } = "127.0.0.1:8080";
    public string ProfilePath => System.IO.Path.Combine(ProfileDirectory, ProfileName + ".yaml");

    public void Validate()
    {
        ValidateExecutable(TunnelClientExecutable, nameof(TunnelClientExecutable));
        ValidateExecutable(BridgeExecutable, nameof(BridgeExecutable));
        if (!System.IO.Path.IsPathFullyQualified(ProfileDirectory))
            throw new ArgumentException("Profile directory must be absolute.", nameof(ProfileDirectory));
        if (!ValidIdentifier(ProfileName, 100))
            throw new ArgumentException("Invalid profile name.", nameof(ProfileName));
        if (!ValidIdentifier(TunnelId, 200))
            throw new ArgumentException("Invalid tunnel identifier.", nameof(TunnelId));
        if (!ControlPlaneBaseUrl.IsAbsoluteUri || ControlPlaneBaseUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(ControlPlaneBaseUrl.UserInfo))
            throw new ArgumentException("Invalid control-plane URL.", nameof(ControlPlaneBaseUrl));
        ValidateLoopbackAddress(HealthListenAddress);
    }

    private static void ValidateExecutable(string path, string parameter)
    {
        if (!System.IO.Path.IsPathFullyQualified(path)
            || !string.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)
            || !File.Exists(path))
            throw new ArgumentException("Executable path must identify an existing absolute .exe file.", parameter);
    }

    private static bool ValidIdentifier(string value, int maximumLength) =>
        value is { Length: >= 1 } && value.Length <= maximumLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static void ValidateLoopbackAddress(string value)
    {
        int separator = value.LastIndexOf(':');
        if (separator < 1 || !IPAddress.TryParse(value[..separator], out var address)
            || !IPAddress.IsLoopback(address)
            || !ushort.TryParse(value[(separator + 1)..], out var port) || port == 0)
            throw new ArgumentException("Health listener must be an explicit loopback address and port.",
                nameof(HealthListenAddress));
    }
}

/// <summary>Transient values from the current boundary. Never persist or log this value.</summary>
public sealed record McpConnectionMaterial(string PipeName, string Capability)
{
    public void Validate()
    {
        if (!Flamoris.Mcp.Core.McpOptions.ValidPipeName(PipeName)
            || Capability is not { Length: 64 } || !Capability.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid managed connection material.");
    }

    public override string ToString() => "McpConnectionMaterial [redacted]";
}

public interface IMcpConnectionMaterialSource
{
    McpConnectionMaterial GetCurrent();
}

public interface IProviderCredentialSource
{
    ValueTask<string> GetControlPlaneApiKeyAsync(CancellationToken cancellationToken);
}

