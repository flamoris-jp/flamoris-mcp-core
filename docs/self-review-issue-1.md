# Issue #1 self-review

## Scope and boundary

- Core references no WPF, 2D, Cutwork or Kachinco assembly.
- No editor session, document copy, history or persistent credential is created.
- Tools are closed host-owned adapters; unknown tools fail closed.
- Hub routing, LAN/cloud, files, process execution and AI providers are absent.

## Security and lifecycle

- Windows pipe combines owner-only DACL and explicit remote-client rejection.
- A 256-bit ephemeral capability is authenticated before MCP protocol bytes.
- Revocation is checked with identity/revision at the serialized commit boundary.
- Deterministic barrier tests cover cancel, timeout, disable and replacement before
  a deliberately uncooperative preparation attempts its late commit.
- Permission downgrade is implemented as revoke plus fresh enable.
- Secret/payload omission and logging failure isolation have focused tests.

## Protocol and resource use

- Official C# SDK 2.2.0 owns modern/legacy MCP lifecycle and discovery.
- Published self-contained bridge tests pin both 2026-07-28 and 2025-03-26 clients.
- Frames, JSON, pending messages, admission, tools and timeouts are bounded.
- Reconnect tests prove the host authority survives bridge replacement.

## UI and assets

- Projection separates endpoint availability from authenticated connection.
- Foreground scopes are idempotent/reference counted; background context is quiet.
- Supplied PNG/WebP bytes have SHA-256 provenance and remain unmodified.
- WPF rendering/cursor ownership remains with each host.

## Evidence and remaining acceptance

The local Work container has no .NET 10 SDK, so it cannot claim a local build or
test run. GitHub Windows CI is the executable evidence for compilation, official
client interoperability, native pipe flags, deterministic rebuild and package
consumption. CI failures must be fixed before review readiness.

Human application integration, visual DPI/cursor acceptance, different-user /
elevation / second-machine checks and production consumer migrations remain
separate consumer work. They are not silently claimed by the synthetic harness.
