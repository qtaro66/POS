# POS closing report workspace

This slice turns existing Branch closing report persistence and Central delivery
into an inspectable POS workflow. It implements read-only saved report browsing,
filtered keyset pagination, delivery diagnostics, frozen snapshot detail and
links from shift history. It does not add Back Office functionality.

## Owning source and read boundaries

The Branch closing_report_outbox is the owning source for the report list and
saved snapshot. Queries never recalculate amounts, financial breakdowns or table
counts from current sessions/menu/master data. Saved schema-1 root JSON and its
PascalCase nested records are projected into typed camelCase HTTP responses.
Unavailable legacy cash/financial/table summaries remain unavailable, not zero.
Report snapshots remain immutable while delivery metadata is read live.

All three new GET routes use the existing complete-identity policy plus PcPos
role validation. Branch identity comes exclusively from the authenticated
principal; no query parameter can select another Branch. Responses are no-store.
Queries do not claim, retry, renew, confirm, retire or delete work. They reuse the
existing atomic Branch command-store transaction boundary but execute only reads.
The browser never connects to SQLite or edits worker configuration.

## Delivery interpretation

Delivered means a matching Central acknowledgement was recorded. InFlight means
a live lease exists, not proof of a network connection. LeaseExpired means an
expired claimant may need recovery; it is not evidence that Central did not
receive the report. RetryScheduled means the next attempt is delayed after a
failure. Queued covers remaining unconfirmed work, including ready retries.

Attempts count claims, not confirmed sends. LastFailure is the last completed
failure category and may remain visible during another claim; it is not the
current attempt's outcome. Only TransportUnavailable, CentralRejected and
AcknowledgmentUnavailable are exposed. Unknown stored categories are sanitized to
null without exposing raw strings, worker IDs, lease tokens or HTTP bodies.

A separate settings response reports whether automatic delivery is configured
for this authenticated Branch. Enabled does not mean Central is online or its
schema/certificate is ready. Disabled also covers configuration for another
Branch and exposes no configured Branch, URL, path, certificate or secret.

## Pagination and consistency

Lists use descending SQLite outbox sequence, with a first-page high-water bound
and an exclusive before position. Later appends are absent from continuation
pages until refresh. Cursor scope includes exact Branch, status, store, shift,
business date and limit. Malformed, duplicate/unknown-field or scope-mismatching
cursors are rejected. Cursor positions are untrusted navigation hints, not
authorization tokens; fixed Branch predicates apply even to forged positions.

Lifecycle companion filtering adds an independent lifecycleStatus predicate to
the original report-status filter. All, Pending, Delivered and NotRecorded are
canonical values. A confirmed base packet does not satisfy lifecycle Delivered;
NotRecorded means no capture and no job, never an orphan or corrupt queue row.
The original list item's deliveryState still describes the base packet only.
Each row now also projects independent lifecycleDelivery metadata, with null
reserved for NotRecorded history. The POS labels both receipts separately and
shows companion queue/lease/retry/confirmation directly in the list. Existing
detail remains available without rewriting the saved report or adding browser
retry. The nested report projection and detail's top-level lifecycleDelivery
are produced from the same read and agree.

The client keeps omitted companion metadata from an older server as unknown.
It requires explicit verifiable metadata to accept a lifecycle-filtered row;
neither a parent receipt nor an omitted property is proof of companion delivery.
All/missing lifecycle filters permit old server responses, while still showing
unknown status. Late filter, page and selection responses remain cancelled or
ignored through the existing query-generation boundary.

Cursor version 2 binds the lifecycle predicate as well as existing scope and
limit. Version-1 continuations require refresh. Each list read validates the
authenticated branch's recorded companion/job consistency within its read
transaction before applying filters. Inconsistency returns unavailable rather
than hiding damaged jobs behind an empty filtered page. Reads never claim work
or change attempt counts, leases, retry schedules or receipt timestamps.
This consistency check scans recorded context metadata for the authenticated
branch before applying the page limit. Its cost grows with branch history;
the page limit bounds returned rows, not all validation work. Measure large
staging histories before rollout; no production-scale latency claim is made.

Delivery state can change between pages. This is a high-water-bounded membership
traversal, not a frozen transaction spanning browser requests. Keyset positions
avoid duplicates from ordinary delivery updates, but status-filter membership can
change; refresh begins a new traversal. The POS clears or cancels old requests
when filters, scope, selection or refresh generation change, preventing another
page/report response from populating the current selection.

## Operational scope

The saved report viewer shows original closing attribution, reconciliation,
recorded cash/bill financials and table counts separately from current delivery
metadata. Following D18.9, aggregate queue counts and automatic-delivery settings
are on the system-status page, not status cards in the main store-and-shift area.
The old aggregate queue card is moved from that area; report-specific delivery
states remain part of the report viewer. A transport outage does not block report reads or lifecycle commands
while the Branch is reachable. List/detail failures do not erase a pending
lifecycle request or alter accepted sales. Queue settings/counts are also useful
on the existing system-status page, separately from table-event Data Sent.

There is no browser Retry, Reprint, export, delete or configuration command in
this slice. Manual privileged retry and physical printing need their own command
and authorization designs. Central ingestion/notification behavior is unchanged.
The original workspace introduced no migration. Subsequent companion delivery
uses Branch schema 35 and Central schema 3; lifecycle list filtering adds no new
migration. Per-row lifecycle metadata extends the read response without changing
saved packets or cursors. D21.3 approval applies to the authenticated read
contracts before merge.
