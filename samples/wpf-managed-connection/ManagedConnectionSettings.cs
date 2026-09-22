using System.Net;

namespace Flamoris.Mcp.Samples.ManagedConnection;

/// <summary>Host-owned non-secret settings for the verified tunnel-client sample.</summary>
public sealed record ManagedConnectionSettings
{
    public bool AutoStart { get; init; }
    public required string TunnelClientExecutable { get; init; }
    public required string ProfileDirectory { get; init; }
    public required string ProfileName { get; init; }
    public required string TunnelId { get; init; }
    public required string BridgeExecutable { get; init; }
    public Uri ControlPlaneBaseUrl { get; init; } = new("https://api.openai.com");
    public string HealthListenAddress { get; init; } = "127.0.0.1:8080";

    public string ProfilePath => Path.Combine(ProfileDirectory, ProfileName + ".yaml");

    public void Validate()
    {
        ValidateExecutable(TunnelClientExecutable, nameof(TunnelClientExecutable));
        ValidateExecutable(BridgeExecutable, nameof(BridgeExecutable));
        if (!Path.IsPathFullyQualified(ProfileDirectory))
            throw new ArgumentException("Profile directory must be an absolute path.", nameof(ProfileDirectory));
        if (!ValidIdentifier(ProfileName, 100))
            throw new ArgumentException("Invalid profile name.", nameof(ProfileName));
        if (!ValidIdentifier(TunnelId, 200))
            throw new ArgumentException("Invalid tunnel identifier.", nameof(TunnelId));
        if (!ControlPlaneBaseUrl.IsAbsoluteUri
            || ControlPlaneBaseUrl.Scheme != Uri.UriSchemeHttps
            || !string.IsNullOrEmpty(ControlPlaneBaseUrl.UserInfo))
            throw new ArgumentException("Control-plane URL must be absolute HTTPS without user information.",
                nameof(ControlPlaneBaseUrl));
        ValidateLoopbackAddress(HealthListenAddress);
    }

    private static void ValidateExecutable(string path, string parameter)
    {
        if (!Path.IsPathFullyQualified(path) || !string.Equals(Path.GetExtension(path), ".exe",
                StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
            throw new ArgumentException("Executable path must identify an existing absolute .exe file.", parameter);
    }

    private static bool ValidIdentifier(string value, int maximumLength) =>
        value is { Length: >= 1 } && value.Length <= maximumLength
        && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-');

    private static void ValidateLoopbackAddress(string value)
    {
        int separator = value.LastIndexOf(':');
        if (separator < 1 || !IPAddress.TryParse(value[..separator], out var address)
            || !IPAddress.IsLoopback(address) || !ushort.TryParse(value[(separator + 1)..], out var port)
            || port == 0)
            throw new ArgumentException("Health listener must be an explicit loopback address and port.",
                nameof(HealthListenAddress));
    }
}

/// <summary>Transient values from the current Core grant. Never persist or log this object.</summary>
public sealed record ManagedConnectionMaterial(string PipeName, string Capability)
{
    public void Validate()
    {
        if (!Flamoris.Mcp.Core.McpOptions.ValidPipeName(PipeName)
            || Capability is not { Length: 64 } || !Capability.All(Uri.IsHexDigit))
            throw new ArgumentException("Invalid managed connection material.");
    }

    public override string ToString() => "ManagedConnectionMaterial [redacted]";
}

public interface IConnectionMaterialSource
{
    ManagedConnectionMaterial GetCurrent();
}

/// <summary>Back this with Windows Credential Manager or another reviewed host/platform store.</summary>
public interface IProviderCredentialSource
{
    ValueTask<string> GetControlPlaneApiKeyAsync(CancellationToken cancellationToken);
}
