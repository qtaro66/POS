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
| tableId | Optional exact original table identity captured in the bill, at most 256 characters |
| orderBatchId | Optional exact captured order-batch identity, at most 256 characters |
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

Rows also include nullable tableId and nullable orderBatchIds. Null batch
metadata means the original payload did not capture items, not a confirmed empty
order. A captured empty array remains distinct. These values come from saved
bill JSON, not current table or order rows. Clients accept omitted older-server
metadata as unknown for unfiltered reads, but require a matching tableId or batch
membership when the corresponding filter is supplied.

Detail shape: schemaVersion=1, branchId, asOf, transaction (the same captured item)
and nullable financials. Financials uses the existing captured StoreBillTotals
fields: itemGrossAmount, lineDiscountAmount, billDiscountAmount,
serviceChargeAmount, totalBeforeRounding, roundingAdjustment, amountDue,
vatInclusiveAmount and amountBeforeVat. Unavailable legacy totals remain null.
No raw bill JSON, current item/master reconstruction or reprint command is exposed.

Detail additionally contains nullable bill. Its version-1 projection contains
branchId, tableSessionId, tableId, paymentRequestId, capturedAt, masterDataVersion,
calculationPolicyVersion, vatRate, serviceChargeRate, items and nullable
billDiscount. PaymentRequestId is the earlier bill-preparation request, not the
cash transaction requestId. A missing old bill projection remains unknown.

Each item contains orderLineId, orderBatchId, sellableItemId, displayName,
quantity (captured chargeable quantity), unitPrice, fulfillmentType, nullable
comment, options, grossAmount, nullable discount and netAmount. Each option has
optionId, displayName, quantityPerItem and unitPrice. Discounts contain kind
(Percentage/FixedAmount), requestedValue, requestedAmount and appliedAmount.
Names and comments are captured text and may contain CR/LF/TAB; render them as
plain text. IDs are bounded to 256 characters, names to 512 and comments to 4096.

Captured bill parsing is bounded to 1 MiB. Invalid modern captures return 503;
legacy absence is null, never recomputed or zero-filled. Validation reconciles
recorded line/aggregate totals and identities without applying current pricing
or rounding policy. Duplicate line IDs are invalid; repeated option IDs are not
deduplicated. No caller receives a partial/truncated bill as if complete.

Cursors bind every filter, branch, limit, schema cookie, initial effective business date and
capture high-water position. Default-date continuations retain their first day
across midnight. A changed filter must start a new query. Newly captured sales
appear after refresh. List and detail may be read at different observation times,
but both use the original saved monetary and identity facts.
VACUUM or schema maintenance invalidates existing cursors with 400; start a new
first-page query. Manual deletion, database replacement or restore also requires
a fresh traversal because this source uses existing rowids and is append-only
through normal application operations.
New continuations use version 2 and bind both original-table and batch filters.
Old version-1 cursors require a fresh first-page query. Database schema versions
and stored captures do not change.

Incomplete identity or non-PcPos roles receive 403. Invalid query/identity receives
400. Missing or foreign-branch detail receives 404 CashHistoryNotFound without
existence disclosure. Unavailable or inconsistent captured storage receives 503
without sensitive error text. Reads do not create audits or mutate settlements,
outbox events, receipt jobs or lifecycle records.

D21.3 approval applies to these new read contracts. Full D18.8 coverage, other
payment methods and authorized receipt/kitchen reprinting remain separate work.
