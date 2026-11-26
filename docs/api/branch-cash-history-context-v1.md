# Cash history recorded context v1

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/shift-context

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/receipt-evidence

Require complete authenticated provisioned PcPos identity and the authenticated
Branch. Accept no body or query parameters, return Cache-Control: no-store, and
remain available independently of cash-test write feature flags. URL-encode the
canonical parent identifier (at most 256 characters, not GUID-only).

Both responses contain schemaVersion=1, branchId, asOf, cashRequestId and
tableSessionId. Missing/foreign parent is 404 CashHistoryNotFound. Incomplete
identity and other roles receive 403. Invalid parent is 400
InvalidCashHistoryIdentity; any query key is 400 InvalidCashHistoryContextQuery.
Corrupt/unavailable evidence is generic 503 without source JSON or internals.

## Shift context

Additional fields: storeSessionId, shiftId, businessDate (yyyy-MM-dd) and entries.
All are scoped to identities captured in the original cash sale, not today's
active lifecycle. Entries are in original recorded sequence, with at most two
records. No matching evidence returns an empty array, not a current-state claim.

Each entry contains requestId, action (OpenStore, OpenShift, CloseShift or
CloseStore), occurredAt (UTC), branchLocalDate (yyyy-MM-dd), employeeId, deviceId,
nullable cashAmount and nullable closing. Opening cash can be absent for
OpenShift; OpenStore requires its original amount. Closing contains closesStore,
expectedCashAmount, nullable actualCashAmount and nullable cashDifference.

Openings have closing=null. Closing cashAmount equals actualCashAmount, and
cashDifference equals the recorded actual minus expected; actual/difference may
both be null for an unreconciled CloseShift. CloseStore requires an actual count.
These are recorded operation totals, not the selected receipt's amount or a
recalculation from current sales. An OpenShift result's inherited earlier closing
is not returned as this shift's closing. Missing operations are not fabricated.

## Receipt evidence

Additional fields: receiptNumber, queuedAt (UTC), capturedPrintStatus with the
original value QueuedForSimulation, and nullable simulation. The queue time is
the saved original cash receipt queue time, not a simulation execution time.

Simulation is {status, text}. Status Simulated requires nonblank plain text up
to 128 KiB UTF-8, preserving CR/LF/TAB; Failed requires text=null. A missing lazy
result table or row returns simulation=null and creates no table. No result
timestamp, content hash or physical-printer status is inferred.

Opening this evidence never renders or simulates a receipt, creates a print job,
retries printing or opens a drawer. Recorded simulation success does not prove
physical printing success. Both contracts require D21.3 approval before merge.
