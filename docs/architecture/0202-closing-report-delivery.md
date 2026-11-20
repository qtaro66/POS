# Branch closing report delivery

This extends the schema-33 closing outbox without another database migration.
The Branch API owns the worker and all SQLite writes. The POS browser only reads
branch-scoped status; failure to read status cannot block lifecycle commands.

## Activation

ClosingReportDelivery is disabled by default. Enable it only after deploying a
compatible Central ingestion service implementing the contract below. Configure
BranchId, CentralOrigin (HTTPS origin only), and BranchCentralMtls with existing
certificate/private-key configuration. BranchOrdering must be enabled and point
to an initialized database at the current schema; the worker uses that exact
ordering database, with no separate report database option. Existing certificate
handling validates TLS normally and disables redirects. Secrets remain outside
version control.

Defaults are a 10-second complete HTTP deadline, 60-second lease, 5-second idle
poll, 30-second initial retry and 5-minute maximum retry. Startup validation bounds
timings and requires a lease longer than the HTTP deadline, ordering database and
mTLS handler. Retry doubles per failed claim up to the maximum. Database failures
retain the durable job; cancellation leaves its lease for expiry/recovery.

## Delivery and acknowledgement contract

POST /api/v1/closing-reports sends the exact saved UTF-8 JSON payload; it is never
rebuilt from current sales. A successful response is HTTP 200 with a JSON object:

```json
{
  "schemaVersion": 1,
  "branchId": "EXAMPLE-BRANCH",
  "reportId": "closing:opaque-stable-identity",
  "status": "Accepted"
}
```

AlreadyAccepted is also a valid status, but only for the same report and branch.
Central must authenticate the Branch certificate, enforce branch identity, durably
persist and deduplicate the exact report, and reject changed payloads for the same
ID before returning either acknowledgement. The acknowledgement is limited to
4096 bytes, rejects duplicate root properties, and is covered by the HTTP deadline
including streamed/chunked body reads. Unknown version, missing/wrong identity,
malformed/truncated/oversized JSON, redirects and non-200 replies cannot confirm a
report. Response bodies and exception details are not logged.

Delivery is at least once. If Central commits but its response is lost, the worker
retries the same payload and ID after the configured delay. Confirmation/retry is
fenced by the current unexpired lease, preventing a stale worker from changing the
new owner's result. Worker restart or transport failure does not participate in closing
transactions; a running offline Branch can still close and enqueue reports.

## POS visibility and remaining integration

The store-and-shift workspace shows independent report queue counts, latest
confirmation and a manual refresh. Counts are separate from table-event Data Sent
status. Pending includes reports currently leased. Unknown status is shown as
unavailable rather than zero or synchronized; no retry/configuration command is
exposed to the browser.

This handoff does not implement Central ingestion/storage, Back Office
notifications or production certificate provisioning. The transport contract is
validated using synthetic HTTP handlers and SQLite, not a deployed Central or
physical devices. Keep delivery disabled until Central implements durable
acknowledgements. No overall Cloud-sync completion claim follows from these tests.
D21.3 approval applies to the delivery contract and security-sensitive wiring.
