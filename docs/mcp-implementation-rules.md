# FLAMORIS MCP Implementation Rules

Status: Project-wide implementation policy  
Applies to: FLAMORIS 2D, Cutwork, Kachinco, Studio and future FLAMORIS applications that expose MCP

This document defines the shared UX, safety, configuration and deployment rules for MCP integration across FLAMORIS applications.

The rules in this document are application-independent. Each application may keep its own MCP settings and application-specific commands, but the user experience, property names and safety boundaries should remain consistent.

---

## 1. Implementation order

MCP implementation depends on the shared FLAMORIS logging foundation.

Do not begin production MCP Core integration until the shared logging library is available and its basic configuration/diagnostic contract is stable.

MCP diagnostics must use the shared logging infrastructure rather than inventing an unrelated logging path.

---

## 2. Connection status must always be visible

Every MCP-enabled desktop application must expose a small, continuously visible connection status indicator in the normal application chrome.

Required visual states:

- 🟢 Connected / MCP available
- 🔴 Disconnected / MCP unavailable

Applications may add additional states when useful, for example:

- 🟡 Starting / reconnecting
- ⚪ MCP disabled

However, green and red remain the common connected/disconnected language across FLAMORIS applications.

The indicator should be visible without opening a settings dialog.

The status reflects the application's MCP endpoint/bridge availability, not an inferred claim that a specific AI identity is actively connected.

A tooltip or status text should explain the current state and, when appropriate, the most recent connection error in user-friendly language.

Connection diagnostics must also be written through the shared logging system under an MCP-specific category.

---

## 3. AI activity must be visible to the user

When an MCP client is actively performing an operation that can affect the current application state, the application should visibly indicate AI activity.

The preferred common behavior is to temporarily change the mouse cursor while the MCP operation is executing.

Requirements:

- the AI activity cursor must be visually distinct from the normal pointer;
- it must be temporary;
- it must return to the correct normal application cursor after the operation completes, fails, times out or is cancelled;
- nested/concurrent requests must not leave the cursor stuck in an AI-working state;
- read-only background health/status checks should not constantly flash the cursor;
- only meaningful foreground MCP activity should trigger the user-visible busy indication.

Applications may additionally show a small activity glyph near the MCP connection indicator.

The user must remain aware that an external agent is currently acting on the document.

---

## 4. MCP failures must never damage user work

This is a primary FLAMORIS MCP invariant.

> A connection, authentication, transport, protocol, timeout or MCP client failure must not corrupt, replace, discard or invalidate the user's document or current editing state.

MCP is an external access layer around the application's existing authority. It is not the owner of the document lifecycle.

Required behavior:

- transport failure must not close or replace the current document;
- MCP restart must not create a replacement editable document;
- authentication failure must not modify application state;
- protocol parse/validation failure must not modify application state;
- cancelled or timed-out mutation requests must not commit later;
- loss of the MCP bridge/server must not invalidate the application's own Undo/Redo history;
- reconnect must attach to the existing authoritative application session rather than silently loading a second copy;
- stale document/session tokens must fail safely;
- stale revisions must fail safely;
- an exception inside MCP infrastructure must be contained at the MCP boundary wherever possible;
- the application must continue to be usable by the human after an MCP failure.

Where an application uses transactions, persistent mutation should commit only after application-level validation succeeds.

The application's existing document/session/history authority remains the source of truth.

---

## 5. MCP must not own application documents

Every application keeps its existing architecture:

```text
Human UI
    -> application Command / Query / Transaction
    -> authoritative application session
    -> document / history

MCP
    -> MCP Core guards / transport
    -> application Command / Query / Transaction
    -> THE SAME authoritative application session
    -> THE SAME document / history
```

Do not introduce:

- a second Project/Document copy for MCP;
- a second editable EditorSession;
- a second Undo/Redo stack;
- MCP-only persistent state;
- hidden auto-save or reload behavior caused by MCP reconnect;
- MCP-specific mutation semantics that bypass the application's normal validation.

---

## 6. Settings belong to each application

Each FLAMORIS application owns and persists its own MCP configuration.

MCP Core provides shared types/defaults/validation where appropriate, but it must not create one global settings file that silently controls every application.

Examples:

```text
FLAMORIS 2D settings
Cutwork settings
Kachinco settings
Studio settings
```

This allows each application to make product-specific choices while preserving a common configuration vocabulary.

---

## 7. MCP setting property names are standardized

Where the same concept exists in multiple applications, use the same property name and meaning.

Recommended common property names:

```text
mcp.enabled
mcp.permission
mcp.transport
mcp.host
mcp.port
mcp.pipeName
mcp.requestTimeoutMs
mcp.maxRequestBytes
mcp.maxConcurrentRequests
mcp.showConnectionStatus
mcp.showActivityCursor
mcp.hub.enabled
mcp.hub.endpoint
mcp.hub.registrationEnabled
```

Recommended values / semantics:

### `mcp.enabled`
Boolean. Enables or disables the application's MCP surface.

### `mcp.permission`
Common values:

- `readOnly`
- `edit`

Applications may define additional explicit permissions later, but must not silently broaden `edit` to arbitrary filesystem/process access.

### `mcp.transport`
Transport identifier such as:

- `streamableHttp`
- `stdioBridge`
- `namedPipe`

This describes the application's local MCP transport shape.

### `mcp.host`
Local bind host where relevant. Production desktop defaults must remain local-only.

### `mcp.port`
Local MCP port where the selected transport requires one.

A dynamically assigned port is allowed and often preferred.

### `mcp.pipeName`
Named-pipe endpoint where applicable.

### `mcp.requestTimeoutMs`
Maximum time allowed for an MCP request before it is cancelled by the MCP boundary.

### `mcp.maxRequestBytes`
Maximum accepted MCP request/body size.

### `mcp.maxConcurrentRequests`
Bounded concurrency limit for MCP requests.

This does not override application mutation serialization.

### `mcp.showConnectionStatus`
Controls the common visible connection indicator.

This should normally default to `true`.

### `mcp.showActivityCursor`
Controls the temporary AI activity cursor.

This should normally default to `true`.

### `mcp.hub.enabled`
Indicates whether the application is configured to participate in MCP Hub routing.

### `mcp.hub.endpoint`
The local MCP Hub endpoint when hub integration is enabled.

### `mcp.hub.registrationEnabled`
Controls whether the application advertises/registers its local MCP endpoint with the Hub.

Property names may be extended as the architecture matures, but existing shared names should not be renamed independently by individual applications.

---

## 8. Secrets are not ordinary settings

Authorization capabilities/tokens are runtime credentials, not normal persisted application preferences.

Rules:

- do not persist ephemeral capability tokens in ordinary application settings;
- do not print credentials in logs;
- do not expose credentials through crash reports;
- rotate/revoke credentials when MCP is disabled;
- rotate/revoke credentials when the authoritative host/session is replaced;
- rotate/revoke credentials on application restart unless a later reviewed design explicitly says otherwise.

---

## 9. MCP Hub compatibility must be considered from the start

The long-term FLAMORIS architecture includes an MCP Hub that can act as a local reverse proxy/router for application MCP endpoints.

Applications and MCP Core should therefore avoid assumptions that only a direct client -> application connection will ever exist.

Target direction:

```text
External MCP client
        |
        v
FLAMORIS MCP Hub
   reverse proxy / routing
        |
        +--> FLAMORIS 2D
        |
        +--> Cutwork
        |
        +--> Kachinco
        |
        +--> Studio
```

The Hub is a separate product/repository and must not become application editing authority.

The Hub routes access to applications. The application remains authoritative.

---

## 10. Reverse-proxy-friendly requirements

To keep future MCP Hub integration straightforward:

### Stable application identity

Each MCP-enabled application should expose a stable product/application identifier distinct from the transient process/session/document identity.

Example conceptual identifiers:

```text
flamoris.2d
flamoris.cutwork
flamoris.kachinco
flamoris.studio
```

Exact identifiers should be standardized before Hub implementation.

### Explicit runtime/session identity

Do not treat process endpoint, port or pipe name as the durable identity of the application.

A restarted process may have a new port/pipe/token while remaining the same FLAMORIS product.

### Discoverable capabilities

The Hub must be able to discover, without knowing editor internals:

- application/product identity;
- application version;
- MCP/Core protocol compatibility information;
- current availability;
- permission mode;
- supported MCP capabilities/tools/resources at an appropriate level;
- current runtime/session identity.

### No absolute direct-client assumptions

Application protocol messages must not rely on assumptions such as:

- the external client always knows the application's dynamic port;
- the external client always connects directly;
- one machine can only run one FLAMORIS application;
- endpoint address equals application identity.

### Forwardable authorization context

MCP Core should keep authorization/permission handling structured so that a future Hub can forward or translate a reviewed scoped authorization context.

Do not solve this prematurely by trusting the Hub unconditionally.

Application-side authorization remains required unless a later security ADR defines a trusted local Hub boundary.

### Useful machine-readable errors

Errors must survive reverse proxying without losing meaning.

At minimum preserve shared error codes for:

- unauthorized;
- forbidden;
- host unavailable;
- stale session/document;
- stale revision;
- busy;
- cancelled;
- timeout;
- invalid request;
- unsupported capability.

---

## 11. Hub failure must not affect documents

The same isolation rule applies to the future MCP Hub.

If the Hub:

- crashes;
- restarts;
- loses registration;
- loses a reverse-proxy route;
- rejects authentication;
- becomes temporarily unavailable;

the host application and the user's document must continue operating normally.

The MCP connection indicator may become red, but the document must remain intact and editable locally.

---

## 12. Logging rules

All MCP implementations should use the shared FLAMORIS logging library.

Use a dedicated MCP category/source so MCP traffic can be filtered separately from normal application diagnostics.

Recommended category shape:

```text
mcp
mcp.transport
mcp.auth
mcp.session
mcp.request
mcp.hub
```

Do not log by default:

- authorization tokens;
- private artwork;
- binary image/video/audio payloads;
- entire Project/Document serialization;
- arbitrary command payloads containing user content;
- file contents.

Useful diagnostic metadata includes:

- product/application identifier;
- runtime/session identifier where safe;
- transport type;
- request kind/tool name;
- result/error code;
- duration;
- revision before/after where appropriate;
- cancellation/timeout state.

---

## 13. UX consistency

Where practical, all FLAMORIS desktop applications should make MCP feel like the same subsystem.

Common expectations:

- the same connection colors;
- similar menu/settings naming;
- the same permission terminology;
- the same shared configuration property names;
- the same MCP-specific logging category family;
- the same broad error semantics;
- the same visible indication when AI is actively changing application state.

Application-specific UI layout may differ, but the mental model should remain consistent.

---

## 14. Priority order

When requirements conflict, use this order:

1. Protect the user's document and editing state.
2. Preserve the application's single authoritative session/history.
3. Keep local security/permission boundaries intact.
4. Keep the human informed about connection and AI activity.
5. Preserve deterministic command/revision behavior.
6. Preserve future MCP Hub reverse-proxy compatibility.
7. Prefer implementation convenience only after the above are satisfied.

A broken MCP connection is acceptable.

A broken user document is not.
