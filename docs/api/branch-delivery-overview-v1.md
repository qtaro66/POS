# Branch delivery overview v1

GET /api/v1/ordering/delivery-overview requires the existing complete Branch,
employee and device identity policy plus PcPos role. The handler derives Branch
scope from the principal, accepts no query parameters or request body and returns
Cache-Control: no-store. It uses the ordering database and no Central connection.

The response has schemaVersion (1), snapshot, tableDeliveryEnabled and
reportDeliveryEnabled. Both flags are booleans, scoped to the current Branch.
The table flag also requires the configured sender database path to normalize to
the ordering path. A false flag can mean disabled or configured for another
Branch/database. Actual settings, remote URLs and credentials are never exposed.

Snapshot fields:

| Field | Meaning |
| --- | --- |
| branchId / asOf | Authenticated Branch and UTC observation reference |
| state | Pending, Current or NoHistory, derived below |
| lastConfirmedAt | Nullable latest receipt across all three sources |
| tableEvents / closingReports | Channel summaries below |
| replayPendingCount | Uncompleted recovery requests |
| replayCompletedCount | Completed recovery request history |
| replayLastConfirmedAt | Nullable latest replay receipt |
| replayOldestPendingAt | Nullable oldest uncompleted replay request |

Each channel contains totalCount, pendingCount, deliveredCount, inFlightCount,
delayedCount, expiredLeaseCount, unknownCount, lastConfirmedAt and oldestPendingAt.
Counts are nonnegative safe integers at the browser boundary. Total equals pending
plus delivered. In-flight, delayed and expired-lease counts are disjoint pending
subsets; unknown metadata can overlap them. Pending includes uncertain attempts,
not necessarily records that Central has never received.

A live lease is in-flight; an expired non-null lease is expired. A future retry or
availability time with no lease is delayed. Unknown table processing labels or
unknown saved report failure categories contribute a count only; raw values are
not returned. Table processing Completed without a receipt timestamp is still
unconfirmed. A valid recorded receipt wins over old processing metadata.

Channel lastConfirmedAt is non-null exactly when deliveredCount is positive;
oldestPendingAt is non-null exactly when pendingCount is positive. The analogous
rules apply to replay counters and timestamps. UTC timestamps accept Z or +00:00
with up to seven fractional digits. Comparisons and global maximum preserve all
seven digits; offsets other than UTC or invalid source times fail the observation.
No rule requires source time to precede asOf because system clocks can change.

State is Pending if either channel has pending work or replayPendingCount > 0.
Otherwise it is Current if any channel/replay has confirmed history; otherwise
NoHistory. A newer table receipt cannot hide an unconfirmed closing report, and
all originals delivered cannot hide outstanding replay. The last-confirmed time
is receipt history, not a global caught-up checkpoint or network health signal.

403 rejects an incomplete identity or non-PcPos role. Query parameters return JSON
400. Missing/incompatible SQLite tables or invalid scoped timestamp data return
503, never partial success or fabricated empty queues. Existing middleware owns
transport enforcement. Cancellation propagates without a successful observation.

Clients should prefer this contract for Data Sent. A modern error, including 404,
must remain unavailable; it must not fall back to an older table-only success.
Legacy clients without a modern reader can show their table-only timestamp with
an explicit coverage label. The endpoint is entirely read-only; all delivery,
retry, lease and payment authorization boundaries are unchanged.
