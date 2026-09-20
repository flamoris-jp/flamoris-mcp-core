# ADR 0001: Core is an access boundary

Status: proposed implementation for Issue #1; review required.

Core owns transient grants, guarded request lifetime, bounded transport and status.
The host owns its existing typed operations, serialization, document and history.
No application assemblies or WPF dependencies enter Core.

An operation prepares without persistent side effects, then invokes a synchronous
commit through RequestContext.Commit on the host serialization lane. That lane
must also serialize human edits and document replacement. Commit checks the
grant, document/runtime identity, expected revision, busy state and deadline under
the same lock as revocation. Host transactions must roll back on failure. Core
cannot roll back arbitrary host callbacks or preempt already committed work.
Cancellation before the commit boundary prevents commit; after it, the committed
result wins. A lost response is ambiguous: query before retry; never replay edits.

Timed-out preparation can complete only as discarded work; its context is sealed.
Admission remains occupied until that work actually exits, bounding stragglers.
Activity is released on timeout/disconnect even while discarded work unwinds.

Authority replacement notifications revoke BEFORE installation, including the
same saved document reopening. Ordinary revision changes do not rotate grants.
Permission changes, disable and shutdown revoke; re-enable creates fresh secrets.
