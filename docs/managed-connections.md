# Managed MCP connections

## Boundary

Managed connection support coordinates a host-owned provider helper without
turning MCP Core into an OpenAI client, WPF settings framework or process manager.

| Core owns | Host application owns |
|---|---|
| Provider-neutral lifecycle contract | Provider selection and implementation |
| Serialized start/refresh/stop ordering | WPF settings UI and localization |
| Revoke-before-stop disable/shutdown | Grant creation and endpoint lifetime |
| Generic, UI-neutral state | Executable/profile/tunnel paths and IDs |
| Secret-safe bounded failure codes | Secure provider credential storage |
| Observer isolation | Status icons, labels and activity UX |

`IMcpConnectionProvider` deliberately has no OpenAI, tunnel, WPF, path or key
properties. Its calls read the application's latest provider-specific material.
Core does not register providers or persist their settings.

## State model

`ManagedConnectionStatus` keeps three independent facts:

- `McpEnabled`: the host reports that a current grant exists;
- `ProviderState`: `Stopped`, `Starting`, `Running`, `Refreshing`, `Stopping`, or
  `Faulted`;
- `ExternalClientConnected`: an authenticated client is attached.

This avoids one ambiguous connected boolean. Important combinations include:

| MCP | Provider | Client | Meaning |
|---|---|---|---|
| disabled | stopped | no | no access |
| enabled | stopped | no | manual connection mode |
| enabled | starting | no | helper launch in progress |
| enabled | running | no | helper available; no authenticated client yet |
| enabled | running | yes | authenticated external client attached |
| enabled/disabled | stopping | no | owned helper cleanup in progress |
| either | faulted | no | safe bounded error; local editor remains usable |

The host projects authenticated attachment from `McpBoundary.Status`. A running
process alone must never be reported as an attached AI client.

## Lifecycle contract

Recommended host flow:

```text
select provider and validate non-secret settings
  -> issue current MCP grant and endpoint
  -> MarkEnabledAsync
  -> StartAsync when auto-start or explicit user action requests it
  -> project helper state and authenticated-client state separately
  -> rotate grant/endpoint in the host
  -> RefreshAsync so the provider consumes the latest material
  -> DisableAsync / ShutdownAsync
       -> revoke grant first
       -> stop only the owned helper process
```

- Concurrent duplicate `StartAsync` calls serialize and produce one provider start.
- `StopAsync` stops the helper while preserving the grant for manual connection.
- `DisableAsync` and `ShutdownAsync` ignore cancellation after cleanup begins:
  revocation and owned-process cleanup must not be abandoned halfway through.
- Provider start/refresh failure invokes provider cleanup and publishes only a
  bounded code such as `provider_start_failed`; provider exception messages and
  inner exceptions are not retained.
- No operation automatically retries or replays an MCP mutation.
- A provider failure must not disable ordinary local editing.

## Configuration vocabulary

Applications may use these common non-secret names:

- `mcp.connection.method`
- `mcp.connection.autoStart`
- `mcp.connection.showStatus`
- `mcp.connection.providers.<providerId>.*`

Provider-specific executable paths, profile names, tunnel IDs and health ports
belong below the provider namespace. The provider ID is a stable application-owned
identifier suitable for a dropdown value.

Do not store these as normal settings:

- current MCP capability;
- control-plane/API key value;
- a command fragment containing secrets;
- a serialized `ProcessStartInfo` environment.

Store long-lived provider credentials in Windows Credential Manager or another
reviewed platform/host secret store. Resolve them only when starting the helper.

## Adding a provider

1. Implement `IMcpConnectionProvider` in the host or copied sample code.
2. Keep provider options in a provider-owned settings type.
3. Read current pipe/capability material on every start or refresh.
4. Validate executable paths and build arguments without a shell.
5. Track the exact child process instance and stop only that instance.
6. Inject transient secrets through a supported secure channel and remove local
   references promptly.
7. Add deterministic tests for duplicate start, refresh, failure cleanup,
   revocation ordering, secret redaction and repeated enable/disable.
8. Document the exact supported provider version and configuration schema.

Do not add a speculative provider to Core. Copy the reference sample and adapt it
in the application until another genuinely shared primitive is proven.

## Current tunnel-client reference

The [copyable sample](../samples/wpf-managed-connection/README.md) records behavior
verified with `tunnel-client v0.0.14`:

- YAML uses `config_version`, `control_plane`, `tunnel_id`, `api_key`, `health`,
  `open_browser`, `log`, and `mcp.commands`;
- `api_key` refers to `env:CONTROL_PLANE_API_KEY`;
- bridge executable and current pipe are in `mcp.commands[].command`;
- `FLAMORIS_MCP_CAPABILITY` is inherited from the owned tunnel-client process and
  is never written to YAML;
- the helper is launched directly with `UseShellExecute=false` and argument-list
  entries `run`, `--profile`, and the validated profile name.

No generic YAML shape is inferred from this one provider.

## First production adoption

Cutwork supplies the initial real-world requirements and is the first planned
production adoption. It currently exposes a manually copied bridge configuration
over `Flamoris.Mcp.Core 1.0.1`. The Core 1.1 sample does not modify Cutwork.
See the [focused follow-up proposal](cutwork-managed-connection-follow-up.md).
