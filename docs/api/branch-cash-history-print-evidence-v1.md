# Cash history original kitchen print evidence v1

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/production-orders/{snapshotId}/print-evidence

GET /api/v1/ordering/transaction-history/cash-sales/{requestId}/production-orders/{snapshotId}/print-jobs/{printJobId}/attempts

Both require complete authenticated provisioned PcPos identity, use the
authenticated Branch, accept no body and return Cache-Control: no-store.
URL-encode path identifiers. Cash request IDs are canonical text up to 256
characters; composite snapshot/job/attempt IDs may contain up to 1024.

Common response fields: schemaVersion=1, branchId, asOf, cashRequestId,
tableSessionId and snapshotId. All timestamps are normalized UTC observations.

## Original evidence

The first route accepts no query parameters and returns an additional nullable
job. A missing matching original job is job=null, not a physical-print outcome.
The current schema supports one original KitchenOrder job for the saved source.
No hypothetical future reprint pagination is included.

Job fields: printJobId, requestId (original submission, not the cash request),
snapshotId, productionStationId, status, attemptCount, createdAt, isReprint=false
and originalPrintJobId=null. Status is one of PrintQueued, Dispatching,
PrintDispatched, Failed, Unknown or legacy Completed. AttemptCount is a
nonnegative Int32. The source station is original, without current printer data.

## Attempt page

| Query | Values |
| --- | --- |
| limit | 1..50, default 20 |
| cursor | Opaque canonical base64url continuation, at most 4096 characters |

The second route additionally returns job (the current observed job metadata),
items and nullable nextCursor. Each attempt contains printAttemptId,
attemptNumber (positive Int32), status, automaticRetryAllowed, startedAt,
nullable firstByteAt and nullable finishedAt. Status is Connecting, Dispatching,
PrintDispatched, Failed or Unknown. Terminal attempts require finish time;
Dispatching/PrintDispatched require first-byte time. Connecting/Failed have no
first-byte time. Retry can only be recorded for a Failed attempt before bytes.

Cursor scope binds Branch, cash capture, session, snapshot, job, source, limit,
schema cookie and initial row high-water position. Ordering follows recorded
sequence, not an invented global clock order. Refresh includes later appends;
active attempt and job status may change between pages. Do not reject a valid
new observation merely because its status/count differs from the earlier page.
Maintenance/restore requires a fresh traversal.

## Errors and interpretation

Missing/foreign parent: 404 CashHistoryNotFound. Missing/out-of-scope snapshot:
404 CashHistoryProductionSnapshotNotFound. Missing/out-of-scope job on the
attempt route: 404 CashHistoryPrintJobNotFound. Other roles/incomplete identity:
403. Invalid path identity: 400 InvalidCashHistoryIdentity. Unknown, repeated,
blank or invalid query values: 400 InvalidCashHistoryPrintQuery. Corrupt source:
generic 503, without source JSON, printer/worker IDs, leases, payload or errors.

PrintDispatched is evidence of sending data, not paper confirmation. Unknown
remains uncertain. Completed is only a stored observation; this route does not
enable a new hardware status capability. Retry flags are read evidence, not
command permission. No retry/reprint/print/drawer command is available here.
D21.3 explicit approval is required before merge.
