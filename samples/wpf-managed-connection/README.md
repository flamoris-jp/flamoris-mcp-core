# Copyable WPF managed-connection sample

This directory is reference source intended to be **copied and adapted** into a
FLAMORIS WPF host. Do not take a production dependency on the sample assembly.
The project exists so CI compiles the example and the Core test project can verify
its security and lifecycle behavior.

The sample is provider-specific application code for `tunnel-client v0.0.14`.
The Core package remains provider-neutral.

## Files to copy

- `ManagedConnectionSettings.cs`: non-secret host settings and secure-source seams;
- `TunnelClientConfiguration.cs`: exact v0.0.14 YAML and Windows argument quoting;
- `TunnelClientProcessHost.cs`: direct launch and exact-child ownership;
- `TunnelClientConnectionProvider.cs`: `IMcpConnectionProvider` adapter;
- `McpConnectionController.cs`: host grant/provider orchestration example.

Rename namespaces and integrate them with the application's existing settings,
localization, logging and shutdown code. Delete seams the host already provides;
the sample is not a framework that must remain structurally identical.

## Host wiring

The host supplies:

1. `IConnectionMaterialSource` over the current `McpBoundary` pipe and active
   `CapabilityGrant`;
2. `IProviderCredentialSource` backed by Windows Credential Manager or another
   reviewed secure store;
3. an issue-grant callback that starts the current local endpoint;
4. a rotation callback that revokes old access and installs a fresh grant/endpoint;
5. a revoke callback for `ManagedConnectionLifecycle` that disables the current
   boundary before helper cleanup.

Create `TunnelClientProcessHost`, `TunnelClientConnectionProvider`,
`ManagedConnectionLifecycle`, then `McpConnectionController`. WPF owns the UI:

```text
Settings dialog
  -> method dropdown / provider options / auto-start
  -> controller.EnableAsync
  -> Dispatcher projection of controller.Changed

McpBoundary.Status.Changed
  -> controller.ProjectExternalClient(boundary.Status.Current.Connected)

permission, document or runtime rotation
  -> controller.RefreshAfterGrantRotationAsync

disable or shutdown
  -> controller.DisableAsync / controller.ShutdownAsync
```

`Changed` is only a notification. Marshal it to the WPF Dispatcher, reread
`Current`, and do not synchronously call another lifecycle method from the event.

## tunnel-client v0.0.14 contract

The profile file is `<ProfileDirectory>/<ProfileName>.yaml`. Configure
`ProfileDirectory` as the actual profile directory used by the installed client
(normally `%APPDATA%\tunnel-client` on Windows). The helper is invoked as separate
argument-list entries:

```text
tunnel-client.exe run --profile <validated-profile-name>
```

The generated YAML shape is:

```yaml
config_version: 1

control_plane:
  base_url: "https://api.openai.com"

tunnel_id: "<application tunnel id>"
api_key: "env:CONTROL_PLANE_API_KEY"

health:
  listen_addr: "127.0.0.1:8080"

open_browser: false

log:
  level: info
  format: json

mcp:
  commands:
    - channel: main
      command: "<quoted bridge executable> --pipe <quoted current pipe>"
```

The process environment supplies:

- `CONTROL_PLANE_API_KEY`: resolved at launch from secure host storage;
- `FLAMORIS_MCP_CAPABILITY`: the current transient grant for bridge inheritance.

The capability is never written to YAML. YAML can retain a stale pipe address, but
it cannot revive a revoked grant without the transient process environment.

## Application-owned settings

Recommended common keys:

```text
mcp.connection.method
mcp.connection.autoStart
mcp.connection.providers.openaiTunnel.executable
mcp.connection.providers.openaiTunnel.profileName
mcp.connection.providers.openaiTunnel.tunnelId
mcp.connection.providers.openaiTunnel.healthListenAddress
```

Store a secure-credential reference if the host needs one; never put the API key
value or MCP capability in ordinary settings.

## Safety notes

- Executable paths must be absolute, existing `.exe` files.
- Profile and tunnel IDs accept only bounded identifier characters.
- Health listeners must be explicit loopback addresses.
- The helper starts with `UseShellExecute=false`; no command fragment is accepted
  from settings.
- Only the exact `IOwnedProcess` returned by the launcher is stopped.
- Provider exception details are replaced by Core's bounded lifecycle error codes.
- Managed helper failure does not change local editor authority.
- Manual connection remains available when auto-start is off or the helper is
  explicitly stopped while MCP remains enabled.

Cutwork is the first planned production adopter. Its implementation remains a
separate application PR; see the [follow-up proposal](../../docs/cutwork-managed-connection-follow-up.md).
