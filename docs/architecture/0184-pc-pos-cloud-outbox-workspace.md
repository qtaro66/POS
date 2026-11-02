# Read-only PC POS cloud outbox workspace

## Scope and requirements

After verified MERGED PR #290, implement D18.10's unacknowledged cloud-outbox
counts and read-only work list while preserving D12.7's durable acknowledgment
and sequence rules. This is a complete slice from the branch database through
authenticated HTTP to the PC POS System page. No send, retry, replay, deletion,
acknowledgment or lease mutation is introduced.

## Source and consistency

The new application query boundary is independent of SQLite/browser/platform
details. Its SQLite adapter opens the existing ordering database in ReadOnly
mode, with no pooling and a deferred read transaction. Counts and rows within
one response share that transaction. The predicate is the authenticated branch
and `successfully_sent_at IS NULL`, rather than a guess from processing status.
Acknowledged records are excluded even if their stored processing label differs.

Counts distinguish Pending, Processing, Failed and Other. Other includes any
unrecognized or inconsistent unacknowledged status; its raw stored label is not
published. A record in Processing does not establish an active/live worker, and
a lease deadline is displayed as stored data without inferring transport state.

Rows contain event/request/session references, action, branch sequence, creation
time, retry count, next scheduled time, lease expiry and the latest recorded
failure time/expected sequence. The latest failure is scoped by branch, event and
sequence; canonical UTC round-trip timestamps are ordered with full fractional
precision and a deterministic failure-ID tie-break. Raw errors, lease-owner
identities, payloads, credentials and connection data are never selected into the
query result or serialized. An expected sequence records a prior Central response,
not the current status of replay or a command to initiate it.

## API and configuration

`GET /api/v1/ordering/cloud-outbox-status` shares the existing BranchOrdering
feature gate, intended database path, complete-identity policy, HTTPS and no-store
guards. The runtime constructs only a read-only query adapter; it does not enable
the independently configured cloud delivery worker or require that worker to run.
This allows inspection of a backlog while delivery is disabled. No new setting,
dependency or database migration is needed.

Branch comes solely from authenticated claims. A single optional `afterSequence`
parameter must be a canonical ASCII positive Int64 decimal string. Caller branch
and limit overrides, repeated/invalid parameters and nonempty GET bodies are
rejected. The page size is fixed at 50. Storage/reader failures return a generic
503; caller cancellation propagates without exposing internal exceptions.

Schema-v1 responses carry the server observation time, branch-wide counts,
bounded items and `nextAfterSequence`. Sequence fields are decimal strings to
preserve the entire Int64 range in JavaScript. The cursor is the last returned
sequence when another page exists, never a branch selection or authorization
capability. Pages are ordered by sequence ascending, so earlier blocking work is
visible first. Each page is a fresh read transaction; counts and records may
change between pages. Refresh starts at the first page. No historical snapshot,
queue-empty heartbeat or successful-connectivity claim is made.

## PC POS lifecycle

The System page displays the counts and one bounded page, reading only on entry,
explicit refresh or next-page action. It does not poll or accumulate pages. The
client reuses bounded same-origin authenticated HTTPS transport, rejects malformed
counts/rows/cursors, validates increasing sequences and public states, and projects
only documented fields into view state. A failed read is not displayed as zero.
Unsupported, loading, unavailable and observed-empty states remain distinct.

Old rows/counts disappear while reading. Ownership and epochs plus cancellation
prevent late results crossing refresh, navigation, screen lock or client identity
replacement. Next-page focus moves to the current list. Navigation preserves the
durable basket and never submits an order. The existing Data Sent acknowledgment
timestamp remains separate; overall System health stays unknown until its other
telemetry exists. Printer/backup health and worker connectivity are not inferred.

## Validation and limits

Safe API/storage tests cover identity and feature gates, HTTPS/body/query guards,
cross-branch isolation, exact Int64 values, latest failure selection, redaction,
generic failure/cancellation, 50+2 real SQLite paging and unchanged database bytes.
The read-only adapter cannot create a missing database. Client/UI coverage includes
protocol bounds, paging, count failures, stale observations, identity replacement,
lock, focus and basket-preserving navigation. Desktop previews use synthetic data
only and remain outside production bundles.

This does not replace operational cloud delivery/replay monitoring, expose Central
event payloads, add queue actions or implement backup/printer health. Query costs
still depend on retained branch data; no cache, counter trigger or new index is
introduced without measured need. D21.3 approval remains required before merge
for public contracts and the authenticated data surface. No automated test uses
production data, cloud services or hardware.
