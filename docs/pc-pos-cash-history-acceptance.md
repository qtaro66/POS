# Captured cash history acceptance

Use synthetic settlements in an isolated Branch database. This slice covers
saved cash settlement history and is not a declaration that all D18.8 history
or physical receipt requirements are finished.

## Coverage

| Requirement area | This slice | Remaining boundary |
| --- | --- | --- |
| D18.8 split view | Cash list and captured detail without a new window | Other transaction sources |
| Search | Business date, receipt substring, session ID, original table ID, captured order-batch ID, Cash/All and Completed/All | Unrecorded display labels, human ticket numbering and other payment/status sources |
| Original money | Tendered, applied, change and available captured bill totals | Missing legacy financial snapshots stay unavailable |
| Original attribution | Capture time, business date, store, shift, employee/terminal IDs and original bill table/policy IDs | Human table labels not present in the capture |
| Bill items | Captured item/option names, chargeable quantities, prices, fulfillment, comments and recorded discounts | Legacy payloads without items and event fields not captured by the separate evidence sources |
| Related evidence | Session status/table-change audit and immutable confirmed-cancellation quantities | Uncaptured cancellation names/value/reason/device, price-override events and other transaction sources |
| D01.4 branch access | Complete identity and PcPos only, branch from principal, no extra PIN | Cross-branch Back Office remains separate |
| Offline scope | Read the local Branch database without a Central call | Branch service/LAN must still be reachable |
| Read-only | No read audit, payment/cancellation/print job or lifecycle mutation | Authorized snapshot reprint is deferred |
| Receipt state | Original simulation receipt number/state is explicit | No physical print, drawer, tax receipt or delivery proof |

## Functional exercise

1. Open Transaction History with only its read capabilities available. Verify
   a split list/detail view, a clear read-only label and the effective business
   date returned by Branch. Blank date defaults consistently with the existing
   Branch workspace (Asia/Bangkok local date).
2. Search a saved receipt using a literal substring, then filter by recorded
   table-session identity, Cash and Completed. Percent/underscore are literal
   characters. Clear filters and start a new first-page query.
   Combine original table identity and captured order-batch identity. These
   filters must use saved bill metadata, not current table/order rows, and remain
   bound in version-2 continuations. Older version-1 cursors require refresh.
3. Load continuation pages, append another settlement, and verify it appears
   only after refresh. Retain all filters and the first effective day on a
   continuation even if the Branch clock crosses midnight. A changed filter or
   forged branch scope must not broaden the result.
4. Select a captured zero-value and ordinary cash settlement. Tendered minus
   applied must equal change; zero is valid. Compare original financial totals
   after current catalog, session and bill-preview changes. Never fill missing
   snapshot values using the current menu or show missing values as zero.
5. Replace the selected row, query, scope or authenticated client while old
   responses are delayed. Old pages/details must not restore stale identity or
   money. Lock the screen and confirm in-flight reads are cancelled.
6. Capture storage/audit/outbox/print-job fingerprints before and after reading,
   searching, paging and selecting detail. They must match. Access must remain
   independent of cash-test write flags and store-open permissions.
7. Open an original provider capture with options, a line discount, bill discount,
   fulfillment type and multiline comment. Preserve saved item order and repeated
   option IDs. Display chargeable quantity rather than an invented original or
   cancelled count. The bill preparation request ID is distinct from the later
   cash request ID.
8. Change catalog, current order and table records after capture; reopen detail
   and compare the entire typed bill and financials. They must remain unchanged.
   Recorded unit price/requested value 1.005 may coexist with rounded stored
   gross/requested amount 1.01. Do not recompute with today's rounding policy or
   hide original precision in the captured detail.
9. Open the related-evidence panel and switch between session/table events and
   cancellation confirmations. Each source starts its own page and retains its
   own cursor scope. Display captured actors and timestamps, not current names
   or terminal guesses. A cancellation may include a zero-cancelled line within
   a partially confirmed event; do not hide that recorded evidence.
10. Change mutable cancellation request status, current table/order data or
    policy records, then reread evidence. Immutable recorded counts and captured
    money remain unchanged. Evidence failure stays within its panel and does not
    erase the original bill/financial detail. A replaced reader during a pending
    continuation must cancel the old request and release loading state.

## Failure and isolation matrix

| Trigger | Required outcome |
| --- | --- |
| No saved capture for the query | Successful empty page, distinct from unavailable |
| Missing or foreign-branch request | 404 without existence disclosure |
| Mobile, Kitchen or incomplete identity | 403 before history reads |
| Duplicate/blank/unknown query or invalid cursor | 400; no broad-query fallback |
| Corrupt selected identity or settlement equation | Unavailable; no partial invented capture |
| Missing receipt-job identity/time | Unavailable; no fabricated capture timestamp |
| Legacy financial snapshot incomplete | Captured cash facts remain visible; financials unavailable |
| Current print simulation later succeeds/fails | Original captured print state stays an observation at capture |
| Read failure after an old successful view | Clear stale detail and display unavailable |
| VACUUM or schema change during traversal | Reject the old cursor and start a fresh first page |
| Legacy payload has no items | Bill and batch metadata remain unknown, not an invented empty bill |
| Explicit complete zero-item capture | Preserve its recorded empty array and coherent zero totals |
| New filtered response lacks matching table/batch metadata | Client rejects it as unavailable |
| Present bill has inconsistent identity, duplicate line IDs or recorded totals | Unavailable, no partial document or source mutation |
| Snapshot exceeds the 1 MiB read bound | Unavailable, never a truncated bill presented as complete |
| Evidence source missing on an older client | Unconnected, not an empty successful history |
| Cancellation evidence has no terminal/name/price snapshot | Unknown fields remain unfilled; no current-data reconstruction |
| Evidence cursor reused for another cash capture, session or source | 400; no broadened or mixed result |
| Late evidence response after source/selection/scope change | Ignore it; captured bill and money remain scoped |

Persistence tests exercise cursor scope, high-water membership, midnight,
legacy identifiers, zero amounts, corruption and immutable read fingerprints.
Bundled SQLite tests also verify schema-cookie change and continuation
invalidation after VACUUM/schema maintenance. Database restore, replacement or
manual capture deletion requires clearing browsing continuations explicitly.
Mapped API tests include the existing Open/Submit/RequestPayment/CompleteCashSale
pipeline followed by history reads, preventing a new read model from rejecting
previously accepted text IDs. Client/component tests cover protocol validation,
split view, search, pagination, late responses and scope/lock behavior. All are
part of the normal tools/verify.ps1 gate.

Itemized-capture tests additionally use the actual BillPreview provider and cash
completion path. They compare snapshots after current-data mutation, preserve
recorded rounded values without rerunning BillCalculator, and exercise original
table/order filters, branch isolation, malformed/oversized bill handling and
cursor version-2 scope. Other event histories and reprint remain deferred.

The related-evidence extension adds a separately versioned source cursor and
tests actual session audit plus full/partial confirmed-cancellation flows before
cash capture. Counts, branch isolation, high-water/maintenance binding and read
fingerprints are checked independently of the original cash list cursor.
See architecture/0210-pos-cash-history-evidence.md and
api/branch-cash-history-events-v1.md. Source streams are not merged into an
invented global timestamp order, and an empty source page is not proof that an
operation never occurred.

Before merge, record D21.3 approval for the new authenticated read contracts.
No database migration, live PostgreSQL, deployed TLS, physical device or
production evidence is required or claimed by these automated checks. Reprint,
refund, reversal, cancellation editing and Back Office/Stock remain outside scope.

## Original production snapshot extension

1. Submit station-routed lines with options, comments and both fulfillment types,
   complete a cash capture, and open its original production evidence. Compare
   names, submitted quantities, option quantities, table and station with the
   saved SubmitOrder payload. No current menu or printer configuration supplies
   replacement data.
2. Confirm a full cancellation before payment. Find its original line from the
   cancellation evidence even when it is absent from the final chargeable bill.
   The detail shows the original station snapshot and clearly distinguishes
   submitted quantity from chargeable quantity.
3. Filter exact batch, line and station identities, traverse multiple pages,
   append another snapshot and refresh. Continuation must exclude later appends
   and reject another subject, filter, limit or SQLite schema cookie.
4. Change current product and station configuration. Original evidence remains
   unchanged. Missing snapshots remain a successful empty result, distinct from
   corrupt, unsupported, oversized or hash-mismatched evidence, which is
   unavailable without clearing captured cash/bill detail.
5. Replace the client, change selection/scope or lock while a page/detail read is
   pending. Ignore old responses and release their loading state. An older client
   without these reads shows unconnected capability, not false empty evidence.
6. Verify branch/session isolation, authenticated PcPos-only reads, no-store,
   malformed query rejection and unchanged database fingerprints. Viewing or
   following a line reference must create no print job, audit or business write.

See architecture/0211-pos-cash-history-production.md and
api/branch-cash-history-production-v1.md. This is captured submission evidence;
it does not prove physical printing or completed production. Reprint execution,
its per-command PIN/permission checks and hardware acceptance remain deferred.

## Recorded shift and receipt context extension

1. Open a durable store/shift, capture a real cash sale and close the shift
   without counting cash. Read its context after another shift and another store
   become current. Preserve the original identifiers, business date, actor,
   terminal, expected cash and null actual/difference values. Do not present the
   later shift/store as belonging to the receipt.
2. Exercise counted closure, closing-only evidence and an OpenShift whose result
   retains the previous closing. Display only the selected shift's recorded
   operations and distinguish missing evidence from a current open/closed state.
3. Open receipt evidence before any simulation. Confirm that the optional result
   table is not created and no write occurs. Run existing successful/failed
   simulation commands as test setup, then read the stored result after changing
   current menu/order data. Preserve the original plain text or failed/null text;
   do not render a replacement or claim physical print success.
4. Verify malformed context, inconsistent totals/status/text, excess operations,
   oversized source data and foreign identities fail safely. Context errors must
   leave the selected cash/bill detail and other evidence panels intact.
5. Refresh or replace readers/selection/scope during pending reads. Hide stale
   context immediately, abort/ignore late responses and retain independent panel
   loading/error states. Older clients show unconnected capability.

See architecture/0212-pos-cash-history-context.md and
api/branch-cash-history-context-v1.md. All evidence remains read-only; no PIN,
audit, simulation, print, reprint or drawer command is introduced by viewing it.
