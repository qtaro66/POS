# Kitchen reprint commands v1

POST /api/v1/ordering/transaction-history/cash-sales/{requestId}/production-orders/{snapshotId}/lines/{orderLineId}/reprints

GET /api/v1/ordering/transaction-history/kitchen-reprints/{commandRequestId}

Both require complete authenticated provisioned PcPos identity, bind the Branch
from authentication, reject query parameters and return Cache-Control: no-store.
URL-encode identifiers. Ordinary identifiers are canonical text up to 256
characters; snapshot and original print-job identifiers can be up to 1024.

## Queue

The JSON body contains exactly these case-sensitive fields:

```json
{
  "schemaVersion": 1,
  "commandRequestId": "12345678-1234-1234-1234-123456789abc",
  "originalPrintJobId": "original-job",
  "reason": "Operator requested a kitchen copy",
  "pin": "<entered for this command>"
}
```

Maximum request body is 64 KiB. Duplicate, unknown and differently cased field
names are invalid. Command IDs use UUID D format and normalize to lowercase;
reuse the same ID and payload after a lost response. Reason is nonblank plain
text of at most 4096 characters; CR, LF and tab are allowed, other control
characters are rejected. Fresh PIN validation and the latest cached permission
are mandatory on every POST, including a replay. The server supplies requester,
approver, device and timestamp. A different authorized active employee may
approve without changing the requester's login session. Never persist the PIN
in a client recovery record or log the request body.

200 returns schemaVersion=1, branchId, requestId (the canonical command UUID),
cashRequestId, tableSessionId, orderBatchId, orderLineId, originalSnapshotId,
originalPrintJobId, reprintSnapshotId, reprintPrintJobId, productionStationId and
queuedAt. These are the original receipt fields on replay. One accepted command
creates one immutable single-line copy and one new job. A new command UUID is
a distinct manual copy and requires fresh authorization. No status of the
original job is treated as permission to resend that job.

## Recover status

GET takes no body or query and requires no PIN. Only the original authenticated
requester and device in the same Branch can read it. 200 returns schemaVersion=1,
branchId, asOf, receipt (the queue receipt), status and attemptCount. Status is
PrintQueued, Dispatching, PrintDispatched, Failed, Unknown or legacy Completed.
AttemptCount is a nonnegative Int32. This is an observation, not proof of paper
output. Unknown must never trigger an automatic new command or resend.

## Errors

Other roles or incomplete identity: 403. Invalid request/UUID/query: 400
InvalidKitchenReprintRequest. Rejected PIN: 401; valid PIN lacking permission:
403. Missing cash capture: 404 CashHistoryNotFound; missing/out-of-scope original
snapshot: 404 CashHistoryProductionSnapshotNotFound; line: 404
KitchenReprintLineNotFound; original job: 404 CashHistoryPrintJobNotFound.
Missing or foreign status: 404 KitchenReprintNotFound. Reusing the command ID
with different target/actor/reason: 409 KitchenReprintConflict. Corrupt or
unavailable authorization/storage evidence: generic 503 without raw records,
PIN, payload, lease or internal exception details.

D21.3 explicit approval is required before merge. PC POS submission and PIN-free
status recovery are documented in architecture/0217-pc-pos-kitchen-reprint-workspace.md.
The client persists intent before sending and never automatically replays a POST.
Physical printer acceptance remains separate and requires operator confirmation.
