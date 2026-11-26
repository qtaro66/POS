# Captured cash transaction history

This is the first operational slice of D18.8 and D01.4 transaction history.
It exposes captured cash settlements already present in the Branch database.
It does not claim complete transaction-history coverage.

## Owning records and boundaries

The owning sources are cash_sales.result_json, its bill_snapshot_json and the
associated simulated_cash_receipt_jobs identity/timestamp written in the same
settlement transaction. Reads do not rebuild old amounts from menu masters,
current table state, current prices or new bill previews. The current writer
captures a simulation receipt number and QueuedForSimulation print state; these
are not evidence of a physical receipt, tax document or printer success.

The list records request/session/receipt identity, original business date,
store/shift attribution, employee/terminal attribution, capture time, cash
tendered/applied/change and captured print status. Completed and Cash describe
this recorded source, not every payment method in the requirements. Amount zero
is valid, matching CashSettlement; tendered minus applied must equal change.

Detail projects the original captured financial totals when complete and
consistent. Legacy unavailable financials stay null rather than becoming zero.
The first projection did not expose captured bill items. The follow-up now
projects original bill items, options, discounts and table identity when the
saved payload contains them. It does not reconstruct original table-number
labels, transfer, cancellation or price-override events from current records.
Table-session and original table-identity filters remain separate from any
unrecorded historical display label.

## Read API and consistency

Two authenticated GET routes list and open captured cash history. The existing
complete Branch/employee/device identity and provisioned PcPos role are required;
Mobile and other roles cannot read completed transaction history. Branch scope
comes only from the principal. No additional PIN, entry reason or read audit is
created. Responses are no-store, with no raw JSON, credential or internal error
text. Existing write-feature flags do not erase access to already stored history.

Filters include one business date, a case-sensitive literal receipt substring,
table-session identity, payment method All/Cash and status All/Completed. Omitted
business date follows the existing Branch workspace convention: Branch time
converted to Asia/Bangkok. A cursor pins that effective date even after midnight.
Legacy canonical text identifiers remain readable; history does not impose a new
GUID-only rule on previously accepted cash-sale identities.

Descending SQLite capture row sequence provides stable keyset navigation. A
first-page high-water bound excludes later captures until refresh. Cursor scope
binds the authenticated branch, raw query filters, resolved date, schema cookie
and page limit.
Cursor values are untrusted navigation positions, not authorization. Every query
retains the fixed branch predicate. Missing companion receipt-job records and
corrupt selected capture metadata fail closed rather than fabricating history.

The cursor uses the existing cash_sales rowid rather than adding a new sequence
column or migration. Ordinary source operations append captures and never delete
them. The bundled SQLite maintenance test confirms VACUUM changes schema_version;
the cursor binds that cookie and requires a new first page after VACUUM or DDL.
Manual deletion, file replacement or restore is outside normal source operations:
clear browsing continuations after such maintenance, even if a restored cookie
matches. Cursor integrity never authorizes a mutation or another branch's data.

## POS workflow

The existing transaction-history navigation opens a read-only split view: list
and filters on the left, selected captured detail on the right. Opening or
searching requires only read capabilities, independent of store commands. Date,
receipt and table-session filters may be changed or cleared. New searches,
refresh, selection, scope replacement and lock cancel or ignore old requests.
Continuation never silently drops filters or merges a different effective day.

There are no cancel, refund, reverse-payment, edit, delete, reprint, export or
drawer controls. The permitted future reprint workflows remain deferred until
their snapshot, authorization, audit and hardware boundaries are implemented.
Back Office and Stock remain separate add-ons.

No schema migration, settlement calculation, print job, outbox or lifecycle
behavior changes in this slice. Branch remains schema 35 and Central schema 3.
The new authenticated read contracts require D21.3 approval before merge.

## Original bill-item projection

SqliteBillPreviewContentSource already captured the bill's items array and table
identity in the JSON copied into cash_sales.bill_snapshot_json. History now
projects this data directly, including original item/option names and prices,
captured fulfillment/comments, line and bill discounts, master/policy versions
and the bill's own capture time. PaymentRequestId identifies bill preparation,
not the later cash settlement request. The two identities must not be equated.

Quantity is the active quantity used for the saved bill, not every originally
ordered or later-cancelled unit. Fully cancelled items are absent from this
snapshot. This projection therefore does not complete cancellation history.

Validation checks captured identities, unique line IDs, nonnegative prices,
quantities, recorded line gross/discount/net and aggregate captured totals. It
does not rerun BillCalculator, derive gross from unit prices/options or recompute
requested discounts: the saved policy version is attribution, not a license to
apply today's rounding rules. Original currency scale and rounding mode are not
stored in this payload. Repeated option IDs remain valid array entries, matching
the owning source; recorded order and quantities are preserved.

Legacy payloads without items retain Bill=null and unknown batch metadata.
Recorded table identity may be available independently; absent identity stays
unknown rather than reading the current table registry. A present malformed bill
fails closed instead of becoming a fabricated empty document. Snapshot parsing
is bounded to 1 MiB; oversized snapshots are unavailable, never truncated or
overwritten. Other identifier/text bounds are documented in the API contract.

List rows add original TableId and distinct OrderBatchIds summaries. Exact
tableId/orderBatchId filters combine with existing receipt/session/date filters.
Cursor version 2 binds them with the existing schema cookie and high-water
position; version-1 continuations require refresh. No database migration or
change to captured JSON is introduced. Older unfiltered responses without new
metadata remain readable as unknown; a filtered result must supply verifiable
matching metadata. Typed bill detail is kept outside the original monetary
snapshot rather than written back into it.
