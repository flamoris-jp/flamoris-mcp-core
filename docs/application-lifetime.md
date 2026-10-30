# Application lifetime attachment (1.4)

`McpOptions.ApplicationLifetime` is opt-in and defaults false. Legacy document-scoped grants keep their revocation semantics. With opt-in, the grant/transport belongs to a single product/runtime; `IMcpHost.Invalidating` must fire for shutdown or full access revocation, not ordinary document replacement.

`HostSnapshot.Available` describes the running application; `HasDocument` describes the target. `mcp.context` is available before a file is opened and reports both, live document token/revision and busy state. Queries and mutation tools require a document by default and return `no_document` without disconnecting. A host may mark a tool `RequiresDocument = false` for application/observation queries; that tool must enforce its own content consent. This flag never grants screen, recording, filesystem or edit permission.

Every document request pins its initial target token. Preparation, read, commit and final query disclosure revalidate that token. Mutations still require runtime, document and expected revision guards. A replacement must generate a fresh token (including reopen of the same file); old guards and in-flight results cannot follow it. Application-lifetime grants never move across runtimes. Explicit stop, permission changes and shutdown still revoke synchronously.

New desktop preferences enable automatic connection at startup using the local read-only Manual method. Persisted `autoConnect: false` remains an explicit opt-out. Configured Hub/tunnel methods reuse their saved settings/credentials. Connection alone never enables screen sharing or operation recording. Failed connections leave the app usable and report a connection failure; no document is created by MCP.

Consumers must use Core and Wpf 1.4.0 together after publication from reviewed main. Host package/bridge and Capture submodule updates must be coordinated. Physical Windows startup, sharing toggles and replacement acceptance are separate from synthetic tests.
