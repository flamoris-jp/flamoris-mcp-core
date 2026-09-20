# Security model

## Protected assets

The user's active document, shared history, application authority, capability
credential and private tool payloads are protected. A broken or hostile MCP client
may lose its connection; it must not replace or damage the active document.

## Local transport

On Windows, pipe creation atomically applies an owner-only protected DACL,
`FILE_FLAG_FIRST_PIPE_INSTANCE`, overlapped I/O and `PIPE_REJECT_REMOTE_CLIENTS`.
The bridge connects to server `.` using `CurrentUserOnly`. The pipe name is an
address, not a secret, and is restricted to the `flamoris-` local namespace.

An additional random 256-bit capability is authenticated in a bounded private
pipe preamble before any MCP bytes reach the SDK. Comparison is fixed-time. The
credential is passed to the child bridge by a dedicated environment variable,
removed at startup, never placed in argv and never persisted by Core.

This boundary does not protect against full compromise of the permitted OS user.
There is no LAN/cloud listener, OAuth server, filesystem browser, process tool,
network tool or generic eval in Core.

## Revocation

Disable, permission change, application restart, authority replacement and
document replacement rotate/revoke the capability. The host must publish
`Invalidating` before installing replacement authority. Existing requests share
the revocation token and recheck it at the commit boundary.

## Resource limits

Pipe and protocol input use strict UTF-8 newline frames with configurable byte,
read and write limits. JSON depth, pending request IDs, tool count, active requests
and deadlines are bounded. Notifications have no response lifetime and are read
one bounded frame at a time rather than counted cumulatively for the connection.
One malformed or failed request is contained at the request/connection boundary
and does not poison later clients.

## Diagnostics and privacy

Only curated fields enter `Flamoris.Logging`: category, transport, outcome, tool
name and duration. Credentials, full request payloads, exception messages,
documents, artwork and media are not forwarded. Logging failure is isolated.

Categories are `mcp`, `mcp.transport`, `mcp.auth`, `mcp.session`, `mcp.protocol`,
`mcp.command`, `mcp.query`, `mcp.request`, and reserved future `mcp.hub`.

## Host obligations

Core cannot make arbitrary host callbacks transactional. Host commit callbacks
must be synchronous and atomic, or roll back before throwing. Adapters must use a
closed reviewed tool registry and independently authorize any future file or
process effect. `Edit` grants only those registered editor operations.
