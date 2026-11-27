# Cash history original production snapshots v1

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/production-orders

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/production-orders/{snapshotId}

Both require complete authenticated provisioned PcPos identity, accept no body,
use the authenticated Branch and return Cache-Control: no-store. Path identifiers
must be URL encoded; snapshot identities are composite text, not GUID-only.

| List query | Values |
| --- | --- |
| orderBatchId | Optional exact original batch identity |
| orderLineId | Optional exact original line identity |
| productionStationId | Optional exact original station identity |
| limit | 1..50, default 20 |
| cursor | Opaque continuation, at most 4096 characters |

Names are case-sensitive. Unknown, repeated, blank, untrimmed or invalid query
values return 400 InvalidCashHistoryProductionQuery. Ordinary identifiers are
bounded to 256 characters; composite snapshot identities to 1024.

Page: schemaVersion=1, branchId, asOf, cashRequestId, tableSessionId, items and
nullable nextCursor. Each item contains snapshotId, orderBatchId,
submittedRequestId, tableSessionId, tableId, productionStationId, submittedAt,
lineCount and orderLineIds. submittedRequestId identifies the original submit,
not the parent cash request.

Detail: schemaVersion=1, branchId, asOf, cashRequestId, tableSessionId, snapshot
(the same item shape) and lines. Each line contains orderLineId, lineSequence,
displayName, quantity, fulfillmentType (DineIn or Takeaway), nullable comment and
options. Options contain nullable optionGroupId, displayName and quantityPerItem.
The quantity is the original submitted quantity. Detail returns the complete
station snapshot, even when a list filter selected one of its lines.

Missing/foreign parent returns 404 CashHistoryNotFound. A missing snapshot under
a valid parent returns 404 CashHistoryProductionSnapshotNotFound. Other roles or
incomplete identity receive 403. Corrupt, unsupported or oversized stored
evidence returns generic 503 without raw JSON, hashes or internal errors.

The reader verifies the stored content hash and schema before returning saved
names, options and comments. No current-data fallback, prices, printer addresses
or physical-print success claim is added. No result means no matching recorded
snapshot was read, not proof that an order never happened.

Continuations bind Branch, cash request, session, exact filters, limit, schema
cookie and original rowid high-water position. Refresh includes later appends.
Maintenance/restore requires a fresh traversal. Cash-list cursor version 2 and
related-event cursor version 1 remain independent and unchanged.

These are read-only contracts with no PIN, read audit or print command. D21.3
approval is required before merge.

Original kitchen print job and paginated send-attempt observations are described
in branch-cash-history-print-evidence-v1.md. They are separate current evidence
reads and do not rewrite this original submission snapshot.
