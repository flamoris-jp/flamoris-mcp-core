# Image result contract

Kachinco #72 requires actual MCP image content, not base64 inside JSON. Core owns
only this transport concern; each host owns rendering, sizing, encoding, admission
and domain metadata. HostImageTool<T> is Query-only; existing JSON tools remain
source compatible. McpImage permits one owned JPEG/PNG of at most 1 MiB.
McpBoundary validates the grant again before disclosing results, including images;
small configured frame budgets return response_too_large. Error responses never
carry an image. McpProtocol.ToWire is also used by HubConnectionProvider so local
pipe -> stdio/tunnel and outbound WSS share the SDK wire names/content types.
The 1 MiB binary cap leaves base64/envelope room below the standard 4 MiB limit.

Tests use the published bridge with an external official SDK client, assert exact
PNG bytes/MIME/metadata and small-budget rejection, and exercise Hub registration,
permission and image-result serialization. A real deployed OpenAI tunnel plus Hub
requires updating the application bundle and static catalog and is separate live
acceptance; no production credentials are required by these deterministic tests.

The Hub envelope uses `McpJsonUtilities.DefaultOptions`, just like the official
SDK transport. Generic Web JSON options serialize the SDK's unset `resultType`
as null, which Python MCP rejects before forwarding even `mcp.context`.
Release 1.3.1 fixes this interoperability regression; regression tests cover
ordinary context, errors and image responses with unset members omitted.

Core/Wpf 1.4.0 is published; the existing release workflow runs after a reviewed
Directory.Build.props version bump reaches exact current main. Tag and manual
release paths remain available; non-main branches cannot publish. Both packages
are built/tested/smoke-checked before NuGet login. This avoids a consumer depending
on a local-only package. No Kachinco CI checkout bypass is introduced.
