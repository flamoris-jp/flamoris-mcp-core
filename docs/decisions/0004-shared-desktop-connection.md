# Shared desktop MCP connection (Core #11 / #12)

The editor owns its boundary, grant issuance and invalidation. Core owns the outbound
Hub provider; Flamoris.Mcp.Wpf owns the shared three-action menu, settings, connection
confirmation, credentials integration, and feedback. There is no desktop listener in
Hub mode and no second editor. Manual and tunnel modes retain the local pipe boundary.

Reverse v1 uses authenticated WSS at `/desktop/{upstreamId}`. A per-upstream bearer
credential is resolved at connection time. No capability leaves the desktop. The first
message registers version=1, productId, runtimeId, documentToken, permission and a
catalog of {name,inputSchema}. Hub pins its catalog in configuration and verifies the
registered schemas before acknowledging {type:"ready",version:1}. One active connector
per configured upstream is allowed; no silent takeover. Hub requests are
{type:"call",id,name,arguments}; replies are {type:"result",id,result} containing an MCP
CallToolResult. JSON limits are 4 MiB, depth 64, one in-flight call per upstream;
responses and calls are never replayed. Cancellation closes the connection, cancelling
uncommitted work. WSS loss cancels the current request before a bounded reconnect
attempt; a revoked grant never reconnects. Read-only grants advertise only queries;
Hub keeps a pinned superset and rejects unavailable calls.

UI: MCP / AI -> Connect..., Stop, Settings... (Japanese/English shared strings).
Manual connects confirm the selected method and previous permission every time.
Auto-connect defaults OFF. Saved permission changes prompt to reconnect; declining
keeps the live grant unchanged. Red/green means disconnected/connected, shown only at
bottom right. Chipsy appears centered for 500 ms after connection or successful
mutation, 48 DIP (twice Cutwork's 24 DIP tool icon), hit-test disabled, no cursor change.

Package release order: Core/Wpf 1.2.0, then three consumer migrations. Source-root
build overrides permit review of consumers against the exact unpublished Core commit.
Physical Windows appearance and real desktop-to-Hub deployment require hands-on QA.
