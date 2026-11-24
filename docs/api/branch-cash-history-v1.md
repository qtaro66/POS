# Branch captured cash history v1

GET /api/v1/ordering/transaction-history/cash-sales lists captured settlements.
GET /api/v1/ordering/transaction-history/cash-sales/{requestId} opens one capture.
Both require complete authenticated identity and provisioned PcPos role, accept
no body, return Cache-Control: no-store and use the authenticated Branch only.

| Query | Accepted value |
| --- | --- |
| businessDate | Optional valid yyyy-MM-dd; defaults to Branch Bangkok local date |
| receiptNumber | Optional canonical literal substring, at most 128 characters |
| tableSessionId | Optional canonical recorded session identifier, at most 256 characters |
| paymentMethod | All (default) or Cash |
| status | All (default) or Completed |
| limit | 1..50, default 20 |
| cursor | Opaque bounded base64url continuation |

Names and enum values are case-sensitive. Unknown, duplicate, blank, untrimmed
or invalid values return 400. Identifiers support the existing canonical text
IDs; a new GUID-only restriction is not imposed. Path identifiers are URL encoded.
Receipt search is literal: percent and underscore are not SQL wildcard syntax.

Page shape: schemaVersion=1, branchId, asOf, effective businessDate, items and
nullable nextCursor. Each item contains requestId, tableSessionId, receiptNumber,
businessDate, storeSessionId, shiftId, employeeId, deviceId, capturedAt,
paymentMethod=Cash, status=Completed, tenderedAmount, appliedAmount, changeAmount
and capturedPrintStatus=QueuedForSimulation. This last value is the originally
captured simulation state, not current printer status. Timestamps are UTC.

Detail shape: schemaVersion=1, branchId, asOf, transaction (the same captured item)
and nullable financials. Financials uses the existing captured StoreBillTotals
fields: itemGrossAmount, lineDiscountAmount, billDiscountAmount,
serviceChargeAmount, totalBeforeRounding, roundingAdjustment, amountDue,
vatInclusiveAmount and amountBeforeVat. Unavailable legacy totals remain null.
No raw bill JSON, current item/master reconstruction or reprint command is exposed.

Cursors bind every filter, branch, limit, schema cookie, initial effective business date and
capture high-water position. Default-date continuations retain their first day
across midnight. A changed filter must start a new query. Newly captured sales
appear after refresh. List and detail may be read at different observation times,
but both use the original saved monetary and identity facts.
VACUUM or schema maintenance invalidates existing cursors with 400; start a new
first-page query. Manual deletion, database replacement or restore also requires
a fresh traversal because this source uses existing rowids and is append-only
through normal application operations.

Incomplete identity or non-PcPos roles receive 403. Invalid query/identity receives
400. Missing or foreign-branch detail receives 404 CashHistoryNotFound without
existence disclosure. Unavailable or inconsistent captured storage receives 503
without sensitive error text. Reads do not create audits or mutate settlements,
outbox events, receipt jobs or lifecycle records.

D21.3 approval applies to these new read contracts. Full D18.8 coverage, other
payment methods and authorized receipt/kitchen reprinting remain separate work.
