# Consumer migration mapping

Production migrations belong in separate consumer repository Issues/PRs. These
mappings prove the Core boundary without moving application domain code here.

## Kachinco

Keep `MainWindow`'s exact `EditorSession`, Command/Query path and shared history.
Replace duplicated access lease, framing, pipe creation and bridge forwarding with
Core. Map the existing UI dispatcher to `InvokeAsync`; fire `Invalidating` before
Open/New replacement and shutdown. Kachinco's approved command registry remains
in Kachinco. Existing file/generation grants stay outside Core.

Topology remains the standard:

```text
stdio bridge -> same-user named pipe -> running WPF -> existing EditorSession
```

## Cutwork

Wrap the existing `EditorSession`, `DocumentToken`, `CutworkDocument.Revision` and
dispatcher. Keep Part/Layer/Mask/Clone/Patch schemas, raster budgets, busy strokes,
transactions and visual query logic in Cutwork. Core supplies grant/revocation,
transport, common errors and projections. Preserve the rule that rollback may
advance the real document revision.

Cutwork is also the first planned consumer of the optional managed-connection
lifecycle added in 1.1.0. Keep settings UI, provider selection, tunnel-client
configuration, secure credential storage and process ownership in Cutwork. Copy
and adapt the reference sample; do not add those application policies to Core.
See [the focused follow-up proposal](cutwork-managed-connection-follow-up.md).

## FLAMORIS 2D

Preserve 2D's reviewed capability, permission, revision and status semantics.
The authority is the Node Product Host behind WPF, so `IMcpHost.InvokeAsync` and
the final Core commit guard must execute at that Product Host authority boundary,
not merely before a C# IPC send. Migrate Streamable HTTP to the common bridge/pipe
only after packaged client and authority-loss tests prove that WPF control loss
revokes/stops the endpoint. Keep Product typed schemas and source-art history in 2D.

No compatibility HTTP endpoint should be retained without a concrete client
blocker and a separate reviewed ADR.
