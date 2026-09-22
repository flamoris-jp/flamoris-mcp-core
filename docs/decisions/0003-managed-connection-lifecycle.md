# ADR 0003: Provider-neutral managed connection lifecycle

Status: accepted for Issue #6 implementation.

## Decision

MCP Core defines a small provider-neutral lifecycle and neutral state projection.
Provider configuration, credentials, process implementation, policy and UI remain
in each host application. A copyable sample records the first tunnel-client
integration without making that provider part of the Core package contract.

The lifecycle serializes start, refresh, stop, disable and shutdown. Disable and
shutdown invoke the host's revoke callback before provider cleanup. MCP grant,
provider process and authenticated-client attachment remain independent state axes.

## Rationale

Transient pipe names and capabilities rotate with enable, permission, document and
runtime lifecycles. A host-managed provider can consume those changes without the
user rewriting configuration, while Core still avoids application settings, WPF,
OpenAI-specific properties or process discovery.

Copyable application code is preferred over a speculative shared provider layer.
Cutwork is the first production adopter; later hosts may prove additional common
primitives through real duplication.

## Consequences

- New provider-neutral types form a compatible minor-version API addition.
- Applications own secure credential persistence and provider validation.
- Provider exception details are replaced by bounded codes at the lifecycle edge.
- Stopping a helper does not necessarily revoke MCP; manual mode remains possible.
- Disable and shutdown revoke before stopping the exact owned helper.
- The tunnel-client sample is compiled and tested but is not packed as a provider.

## Rejected alternatives

- A shared WPF settings panel: it couples Core to product UI and localization.
- An OpenAI-specific Core manager: it prevents provider-neutral adoption.
- Generic command strings from settings: they create implicit process authority.
- Process-name cleanup: it can terminate unrelated user processes.
- Persisting current capabilities in YAML: stale files could retain credentials.
