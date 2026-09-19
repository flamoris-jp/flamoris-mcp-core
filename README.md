# FLAMORIS MCP Core

Shared MCP infrastructure for FLAMORIS applications and tools.

Part of the **FLAMORIS Commons** shared infrastructure family.

## Purpose

FLAMORIS MCP Core will provide reusable infrastructure for exposing FLAMORIS applications to MCP clients without creating a second editing authority.

The initial shared boundary is expected to include:

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

Initial repository foundation. API and transport boundaries are not yet frozen.

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
