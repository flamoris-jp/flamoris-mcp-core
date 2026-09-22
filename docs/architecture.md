# Architecture

## Dependency direction

```text
2D / Cutwork / Kachinco / Studio
  -> application typed Command / Query / Transaction adapters
  -> Flamoris.Mcp.Core
  -> Flamoris.Logging 1.0.0

external client -> Flamoris.Mcp.Bridge -> local named pipe -> Core endpoint
```

Core has no WPF or FLAMORIS editor assembly reference. It owns neither an editor
domain model nor persistent state. The future Hub is outside this graph.

An optional managed helper remains a separate host integration layer:

```text
host settings/UI -> host provider implementation -> ManagedConnectionLifecycle
                                                -> provider helper child process
McpBoundary.Status ------------------------------> host UI projection
```

Core defines lifecycle ordering and neutral state only. Provider settings,
credentials, process configuration and method selection stay in the host/sample.
The managed helper never becomes document, session or permission authority.

## Authority and commit boundary

`IMcpHost.InvokeAsync` is the application serialization hook. It must enter the
same lane used by human gestures, document replacement and Undo/Redo. `Snapshot`
is read only on that lane. Core compares product, runtime, document and expected
revision immediately before a mutation's one synchronous atomic commit callback.

Tool preparation may run asynchronously but must have no persistent side effects.
`RequestContext.CommitAsync` is the only commit gate. A tool may commit once. A
cancelled, timed-out or revoked context is sealed and cannot commit later. Core
keeps an admission slot occupied until deliberately uncooperative preparation
actually exits, preventing unbounded timed-out stragglers.

After the commit point cancellation cannot undo committed user work. A caller
that loses the response must query current state before retrying; transport never
automatically replays a mutation.

## Protocol and discovery

Official `ModelContextProtocol.Core` owns lifecycle, version negotiation, tool
discovery and JSON-RPC. Core adds resource and envelope bounds around its streams.
Host tools publish exact host-owned JSON schemas. Read-only discovery omits edit
tools, while server-side permission checks still reject direct calls.

`mcp.context` returns current safe product/runtime/document/revision/permission
metadata. It never returns a credential, document body or application payload.

## Lifecycle

- Explicit enable creates a 256-bit grant bound to current product/runtime/document.
- Permission changes are disable plus fresh enable.
- Disable, pre-replacement invalidation and shutdown revoke immediately.
- Pipe disconnect changes availability only; it does not replace application state.
- Reconnect attaches to the same grant and existing session while it remains valid.
- Application restart creates a new runtime and grant.

Managed-provider state is independent from the grant and endpoint state. A host may
have MCP enabled while its managed provider is stopped (manual mode), or have a
provider running before an authenticated external client is attached. Disable and
shutdown revoke the grant before stopping the owned provider helper.

## UI projection

`StatusProjection` is UI neutral. Green means enabled endpoint available; red means
unavailable. `Connected` separately indicates an authenticated bridge. Foreground
activity is reference-counted and cleared on endpoint loss. Background context and
health work does not flash the activity cursor. Application UI owns rendering,
thread marshaling, cursor restoration and accessible text.
