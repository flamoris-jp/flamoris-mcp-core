# ADR 0002: stdio bridge and local named pipe

Status: proposed implementation for Issue #1; review required.

External MCP client -> self-contained bridge -> already-running host named pipe
-> existing authoritative application session. One connected bridge per endpoint;
bounded concurrent requests within it. Reconnecting never constructs a document.

Use official ModelContextProtocol.Core 2.2.0, checked 2026-09-19 against its tagged
source and the current 2026-07-28 specification. Leave SDK ProtocolVersion unset:
the SDK handles modern per-request metadata/server discovery and legacy initialize.
Do not implement protocol version negotiation ourselves. No SSE/HTTP listener.

The private pipe preamble authenticates an ephemeral 256-bit capability before
passing any bytes to the MCP SDK. It is NOT an MCP initialize handshake. The
bridge reads capability from its inherited environment, clears it immediately,
and never places it in argv/stdout/logs. The host explicitly provisions it to a
local client launcher; normal persisted preferences must not contain credentials.

Windows server creation atomically installs a protected owner-only DACL,
FILE_FLAG_FIRST_PIPE_INSTANCE and PIPE_REJECT_REMOTE_CLIENTS in dwPipeMode.
The client uses CurrentUserOnly and server '.'. Non-Windows named pipes use
CurrentUserOnly for portable tests. Same-user account compromise is out of scope.

References:
- https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/stdio
- https://modelcontextprotocol.io/specification/2026-07-28/basic/versioning
- https://github.com/modelcontextprotocol/csharp-sdk/tree/v2.2.0
- Kachinco ADR 0003 / Issue #13: explicit native local-only creation and lease.
- Cutwork ADR 0002 / Issue #33: bounded SDK streams and precommit revocation.
- 2D ADR 0010 / Issue #102 / merged PR #103: fail closed on control-channel loss.

No consumer production migration occurs in this PR. In particular, 2D's Node
ProductHost authority requires guards at its actual commit boundary, not merely
before a C# IPC send; see the migration document.
