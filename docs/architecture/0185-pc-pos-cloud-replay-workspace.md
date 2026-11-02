# Read-only PC POS sequence replay workspace

## Scope

After verified MERGED PR #291, add the separate sequence-replay work list to
D18.10's System page. Preserve D12.7 and the existing replay-store contract:
recovery requests do not delete/reset the original event's durable acknowledgment.
Pending replay requests must therefore remain visible even when their original
events are absent from the ordinary unacknowledged-outbox list.

## Source and count meaning

A platform-independent application query boundary exposes only observations. Its
SQLite implementation opens the configured ordering database in ReadOnly mode
and reads summary/rows in one deferred transaction. Pending means a replay request
with completed_at NULL, independent of the original event's acknowledgment time.
Completed requests and other branches are excluded. Original request/session/action
references come from the retained event; no event payload is read or published.

The summary shows pendingCount, leasedCount, unleasedCount and withRetriesCount.
Leased/unleased partition pending work using stored lease-owner presence, including
missing lease records as unleased. An expired lease is still a recorded lease:
these counters do not infer a live worker or transport operation. WithRetries is
the subset with a positive stored failure/retry counter and overlaps both lease
groups; it must not be added to them. Replay and ordinary-outbox counts are also
kept separate rather than presented as one combined cloud backlog.

Rows expose recovery/event/request/session references, expected/received sequence,
requested time, lease presence/deadline, retry count/scheduled time and latest
recorded failure time/expected sequence. Latest failure selection is scoped by
recovery, branch and event sequence, ordered by canonical UTC round-trip time and
failure ID with all seven fractional digits preserved. Raw errors, lease-owner
identities, credentials, payloads and database details are excluded.

## API and pagination

GET /api/v1/ordering/cloud-replay-status shares the existing BranchOrdering gate,
database, complete-identity authorization, HTTPS and no-store guards. No delivery
or replay worker is enabled or needed for inspection. The handler derives branch
only from claims, rejects nonempty/unknown-length GET bodies and accepts only one
optional canonical positive Int64 afterSequence parameter. Caller branch/limit
overrides are rejected; page size is fixed at 50. Reader failures become generic
503 responses while request cancellation propagates. No migration or new setting
is introduced.

Public schema-v1 sequence values are decimal strings, retaining the full Int64
range in JavaScript. Rows are ordered by expected sequence ascending. The existing
unique active-request index makes that key sufficient for pagination within a
branch. NextAfterSequence is the last returned expected sequence when more work
exists. Each page is a fresh read transaction; it is not a historical snapshot.
Requests/counts can change between pages, and refresh returns to page one. The
cursor filters reads and never grants branch access or operational permission.

## Desktop lifecycle

The System view reads on entry and explicit refresh/next-page actions, not by
polling. The existing bounded authenticated HTTPS transport and response validator
reject malformed counts, invalid ranges, inconsistent lease fields, duplicate
identities, non-increasing keys or cursors not matching the last row. Only public
fields enter the view. Loading clears old rows/counts; unsupported, unavailable
and observed-empty results are distinct. Empty work is not an overall Cloud
health claim. Failed/last-expected metadata is historical, not current replay
progress or a new recovery instruction.

Ownership, cancellation and epochs prevent stale results across refresh, page
requests, navigation, lock or identity replacement. Keyboard focus moves to the
new page list. Navigation preserves the durable basket and submits no order.
The ordinary-outbox panel now explicitly identifies its separate queue scope.
Data Sent retains its original acknowledgment semantics and overall System health
remains unknown pending its other telemetry.

## Evidence and limits

Safe API/storage tests cover feature/identity/HTTPS/body/query guards, exact Int64
values, redaction, generic failure/cancellation, missing-database protection,
50+1 real SQLite pagination, branch/completion filtering, missing/expired lease
records, overlapping counts and full-precision failure metadata. The same fixture
has zero ordinary unacknowledged events and 51 pending replay requests; database
bytes remain unchanged after both diagnostic reads, retaining acknowledgment,
request, failure and lease evidence.

Client/UI tests cover protocol bounds, public projection, separate counts,
refresh/paging/focus, failure versus zero, stale reads, identity change, lock and
basket preservation. Desktop visual checks use synthetic development fixtures
excluded from production bundles; no hardware or production service is accessed.

There is no replay/send/retry/acknowledgment, completion, lease acquisition/release
or deletion action. Existing recovery scheduling and financial/business rules are
unchanged. Printer/backup/checkpoint telemetry, worker health, operational commands
and production login/bootstrap remain separate. Read-query costs still depend on
retained data; no counter trigger, cache or index is added without measurement.
D21.3 approval is required before merge for contracts and authenticated diagnostics.
