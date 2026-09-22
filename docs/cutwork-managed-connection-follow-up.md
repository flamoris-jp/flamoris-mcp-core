# Proposed Cutwork follow-up: adopt managed MCP connection sample

This is the focused application-side work identified by MCP Core Issue #6. It is
not implemented in the Core repository.

## Proposed Issue title

Adopt the managed MCP connection lifecycle and tunnel-client reference sample

## Goal

Make Cutwork the first production integration of `Flamoris.Mcp.Core 1.1.0`
managed connections so ordinary pipe/capability rotation no longer requires a
human to rewrite or recopy transient connection data.

## Scope

- update the package reference to Core 1.1.0;
- copy/adapt `samples/wpf-managed-connection` into Cutwork-owned code;
- keep `MainWindow`/`EditorSession`/`McpBoundary` as authority;
- add an application-owned connection-method selector with manual mode preserved;
- persist only non-secret provider preferences and auto-start policy;
- retrieve the control-plane key from reviewed Windows/host secure storage;
- issue/rotate the Cutwork grant and endpoint before starting/refreshing the helper;
- project MCP enabled, helper running and authenticated client connected separately;
- preserve Cutwork's existing red/green status and Chipsy activity behavior;
- revoke before stopping the owned helper on disable, permission change, document
  replacement and shutdown;
- retain the existing copy-configuration action as a manual fallback.

## Security and compatibility

- do not persist `FLAMORIS_MCP_CAPABILITY` in settings or YAML;
- do not put API/capability values in argv, logs, exceptions or diagnostics;
- invoke the configured executable directly, never through a shell;
- stop only the child process started and tracked by this Cutwork window;
- validate profile/executable/bridge paths and use the exact v0.0.14 schema;
- preserve all current Core 1.0.1 idle/framing/cancellation semantics;
- do not change Cutwork domain tools, shared history or `.flimg` behavior.

## Tests

- manual mode and auto-start mode;
- first start and duplicate start;
- current pipe/capability consumed after permission/document rotation;
- revoke-before-stop ordering;
- helper start/crash/stop failure leaves Cutwork editing usable;
- repeated enable/disable leaves no owned helper process behind;
- unrelated tunnel-client processes are untouched;
- profile YAML schema and path-with-spaces quoting;
- no provider key/capability in YAML, logs, exceptions or diagnostics;
- existing Windows published-editor/bridge smoke remains green.

## Out of scope

- changing MCP tools or document authority;
- shared WPF UI in Core;
- automatic tunnel-client installation/update;
- daemon/public service behavior;
- other speculative providers.
