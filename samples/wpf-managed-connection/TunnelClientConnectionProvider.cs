using Flamoris.Mcp.Core;

namespace Flamoris.Mcp.Samples.ManagedConnection;

/// <summary>Provider-specific reference code. Copy and adapt this into the host application.</summary>
public sealed class TunnelClientConnectionProvider(
    ManagedConnectionSettings settings,
    IConnectionMaterialSource materialSource,
    IProviderCredentialSource credentialSource,
    TunnelClientProcessHost processHost) : IMcpConnectionProvider
{
    public string Id => "openai-tunnel-client-v0.0.14";

    public async ValueTask StartAsync(CancellationToken cancellationToken)
    {
        ManagedConnectionMaterial material = materialSource.GetCurrent();
        material.Validate();
        string apiKey = await credentialSource.GetControlPlaneApiKeyAsync(cancellationToken);
        await processHost.StartAsync(settings, material, apiKey, cancellationToken);
    }

    public async ValueTask RefreshAsync(CancellationToken cancellationToken)
    {
        await processHost.StopAsync(CancellationToken.None);
        await StartAsync(cancellationToken);
    }

    public ValueTask StopAsync(CancellationToken cancellationToken) =>
        processHost.StopAsync(cancellationToken);
}
