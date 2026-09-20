# FLAMORIS MCP Core

Shared MCP infrastructure for FLAMORIS applications and tools.

Part of the **FLAMORIS Commons** shared infrastructure family.

## Purpose

FLAMORIS MCP Core provides reusable infrastructure for exposing FLAMORIS applications to MCP clients without creating a second editing authority.

> MCP Core provides local connection, authorization, permission, session and revision infrastructure. The application remains the editing authority.

> FLAMORIS desktop applications use the common red/green MCP status language and shared AI activity indication policy.

The shared boundary includes:

- transport and connection primitives;
- local-only / same-user security helpers where applicable;
- permission and capability grants;
- credential and revocation lifecycle;
- live attachment to a running application session;
- revision/conflict helpers;
- typed protocol/result primitives;
- bounded diagnostics and activity reporting.

Application-specific Commands, Queries, tools, domain models, and Undo/Redo semantics remain owned by each application.

## Core invariant

**MCP is an adapter over the application's authoritative session, not a second editor.**

FLAMORIS 2D, Cutwork, Kachinco, Studio, and future applications must continue to own their persistent state and domain behavior.

```text
MCP client -> stdio bridge -> same-user local pipe -> running application
                                                   -> existing typed operation
                                                   -> existing session/history
```

The bridge and Core never load a project or create an editor session.

## Packages and runtime

- `Flamoris.Mcp.Core`: .NET 10 library; host facade, scoped capability, guards,
  request lifetime, official MCP server integration and UI-neutral projections.
- `Flamoris.Mcp.Bridge`: self-contained console executable that authenticates and
  forwards bounded stdio frames to the explicitly selected running host.
- `assets/runtime`: canonical red/green status images and Chipsy activity sheet.

Stable package: `Flamoris.Mcp.Core 1.0.0` from the FLAMORIS GitHub Packages feed.

~~~xml
<PackageReference Include="Flamoris.Mcp.Core" Version="1.0.0" />
~~~

Core references `Flamoris.Logging` 1.0.0 through NuGet. It does not vendor the DLL.
The MCP protocol implementation uses official `ModelContextProtocol.Core` 2.2.0
and supports its modern 2026-07-28 and legacy initialization paths.

See [package consumption](docs/package-consumption.md) for feed authentication,
consumer CI, bridge packaging, and the tag-driven release contract.

## Host integration summary

1. Implement `IMcpHost` over the existing authoritative application session.
2. Register closed, typed `HostTool<T>` adapters. Do not expose generic JSON mutation.
3. Create `McpBoundary`, explicitly enable Read only or Edit, then start
   `LocalMcpEndpoint` with the returned transient grant.
4. Launch the packaged bridge with `--pipe <name>` and pass the capability only
   through `FLAMORIS_MCP_CAPABILITY`; the bridge removes it from its environment.
5. Revoke before document/session replacement and shutdown via `Invalidating`.

See [host integration](docs/host-integration.md), [architecture](docs/architecture.md)
and [security](docs/security.md). Consumer migrations remain separate PRs.

## Settings vocabulary

Hosts persist their own non-secret preferences using `mcp.enabled`,
`mcp.permission`, `mcp.transport`, `mcp.pipeName`, `mcp.requestTimeoutMs`,
`mcp.maxRequestBytes`, `mcp.maxConcurrentRequests`,
`mcp.showConnectionStatus`, and `mcp.showActivityCursor`.
Capabilities are runtime credentials and are never ordinary settings.

The common transport value is `stdioBridge`; it denotes stdio plus the local
named-pipe attachment, not a bridge-owned editing process.

## Build and test

```powershell
dotnet restore Flamoris.Mcp.slnx
dotnet build Flamoris.Mcp.slnx -c Release --no-restore
dotnet publish src/Flamoris.Mcp.Bridge -c Release -r win-x64 --self-contained true
dotnet test tests/Flamoris.Mcp.Tests -c Release --no-build
```

The Windows CI runs official-client transport tests through the published bridge,
deterministic rebuild checks, NuGet packing and a consumer PackageReference smoke.

## Design principles

- one authoritative application session;
- UI and MCP share the same mutation/history path;
- explicit permissions with revocation;
- least-privilege local access by default;
- stable IDs and typed contracts;
- explicit revision/conflict handling;
- bounded resource use;
- no filesystem or process authority unless a host explicitly grants it;
- transport/security infrastructure must remain separable from application-specific tools.

## Related repositories

- [FLAMORIS Commons](https://github.com/flamoris-jp/flamoris-commons)
- [FLAMORIS Logging](https://github.com/flamoris-jp/flamoris-logging)
- [FLAMORIS 2D](https://github.com/flamoris-jp/flamoris-2D)
- [FLAMORIS Cutwork](https://github.com/flamoris-jp/flamoris-cutwork)
- [FLAMORIS Kachinco](https://github.com/flamoris-jp/flamoris-kachinco)

A standalone MCP Hub may use this repository in the future, but the Hub is not part of MCP Core and applications must not depend on the Hub to expose MCP capabilities.

## Status

Stable 1.0 API line. Breaking public API changes require a new major version.
Application-specific MCP tools and editor-domain behavior remain outside Core.

## License

Code in this repository is licensed under the [Apache License 2.0](LICENSE), unless otherwise noted.

Commercial use does not require permission. If you'd like, we'd be happy to hear what you used FLAMORIS for. This is completely optional.

FLAMORIS software is provided as-is and does not include guaranteed individual support. AI-assisted self-support is encouraged.

If FLAMORIS helps you or you find it interesting, your support helps fund development and keeps the project growing. 🌱  
<sub>Mostly GPU bills.</sub>

---

## 日本語

FLAMORIS MCP Coreは、FLAMORISアプリ・ツール共通のMCP基盤です。

transport、権限、live attach、revision/conflict、revocationなどの共通インフラを提供しますが、各アプリ固有のCommand、Query、domain model、Undo/Redoのauthorityは各アプリ側に残します。

**MCPはアプリ本体のauthoritative sessionへのadapterであり、第二のeditorにはしません。**

商用利用に許可は不要です。もしよければ「こんなのに使ったよ」と教えてもらえるとうれしいです。もちろん強制ではありません。

困ったときは、README、Issue、テスト、ソースコードをAIに読ませて自己サポートしてください。

もしお役に立てたり、面白いと思っていただけたなら、開発費用をご支援いただけるとうれしいです。  
FLAMORISは元気になって育ちます。🌱  
<sub>主にGPU代とか。</sub>
