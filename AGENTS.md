# AGENTS.md

This repository is part of the FLAMORIS Commons shared infrastructure family.

Read and follow:

- `flamoris-jp/flamoris-commons/AGENTS.md`
- `flamoris-jp/flamoris-commons/docs/repository-policy.md`

## Repository-specific rules

FLAMORIS MCP Core provides reusable MCP infrastructure, not application-domain behavior.

- MCP is an adapter over a host application's authoritative session, never a second editor or project copy.
- Do not introduce an independent EditorSession, Project, Undo/Redo history, or persistent domain model.
- Application-specific Commands, Queries, tools, and domain schemas stay in the host application.
- Shared APIs should focus on transport, permission/capability grants, revocation, session attachment, revision/conflict handling, protocol primitives, and bounded diagnostics.
- Local transport must prefer least privilege and same-user/local-only access where applicable.
- Credentials and grants must have explicit lifecycle and revocation.
- Do not expose filesystem, process, network, or import/save authority implicitly.
- Mutating operations must support explicit expected-revision/conflict behavior where the host provides revisions.
- Resource use, message size, concurrency, queues, timeouts, and diagnostics must be bounded.
- Keep MCP Core independent of any future standalone Hub. Applications must not require the Hub in order to expose MCP.

Before extracting code from 2D, Cutwork, or Kachinco, compare the reviewed implementations and move only proven common infrastructure.
