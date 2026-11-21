# Closing report ingestion v1

POST /api/v1/closing-reports is an internal HTTPS-only Branch-to-Central endpoint.
It is mapped only when CentralClosingReports:Enabled is true and uses the existing
Central Cloud BranchCertificate authorization policy. Browser staff sessions and
body-supplied Branch IDs do not establish transport authority.

Request Content-Type is application/json (JSON-compatible media types accepted by
ASP.NET are also supported); maximum complete UTF-8 body is 65536 bytes, including
whitespace. JSON max depth is 32. Duplicate property names and malformed Unicode
are rejected recursively. Names and identifiers are case sensitive.

The request is the exact saved schema-1 outbox payload, with these required root
properties:

| Property | Type and rule |
| --- | --- |
| schemaVersion | Integer 1 |
| reportId | closing: followed by uppercase UTF-8 hex Branch ID, colon, uppercase UTF-8 hex request ID; maximum 2000 ASCII characters |
| branchId | Authenticated Branch; canonical nonempty ID |
| requestId | Original committed Branch request ID |
| storeSessionId / shiftId | Original store and shift identity |
| operation | CloseShift or CloseStore |
| businessDate | Valid yyyy-MM-dd business date |
| closing | Original object with persisted PascalCase property names |
| cashSummary | Original object, or explicit null for unavailable legacy data |

Canonical identity strings have no outer whitespace or control characters and are
at most 256 UTF-16 code units. Unknown fields are preserved, not interpreted.
The closing object requires matching StoreSessionId and ShiftId, ClosesStore
matching operation, nonnegative ExpectedCashAmount, nullable nonnegative
ActualCashAmount and nullable signed CashDifference. Monetary fields use decimal
values with at most two significant fractional places. CashDifference must equal
actual minus expected, or be null when actual is null. CloseStore requires actual;
CloseShift may omit reconciliation by setting both nullable fields to null.
Optional ReconciliationStatus must match Reconciled or NotReconciled respectively.
Context requires EmployeeId, DeviceId, a valid BranchLocalDate and UTC OccurredAt
using yyyy-MM-ddTHH:mm:ss[.1-7 digits]Z or +00:00. These are saved Branch attribution,
not independently authenticated Central staff claims. Financial/table summaries
are opaque immutable snapshots, not recalculated or interpreted by this receiver.

## Responses

All handler responses use Cache-Control: no-store. A successful HTTP 200 body is:

```json
{
  "schemaVersion": 1,
  "branchId": "EXAMPLE-BRANCH",
  "reportId": "closing:stable-opaque-identity",
  "status": "Accepted"
}
```

AlreadyAccepted replaces Accepted only when exactly one durable record has the
same identities, metadata and original JSON text. Success follows transaction
commit. Whitespace/order/escaping changes under an existing identity are a 409,
even if parsing gives equivalent values. Clients retry original bytes and accept
only an acknowledgement matching their Branch/report identity and schema.

| Status | Meaning | Branch behavior |
| --- | --- | --- |
| 400 | HTTP transport or invalid envelope | Retain report, investigate |
| 401 | Missing/ambiguous authenticated Branch | Retain, repair authentication |
| 403 | Authenticated Branch differs from payload | Retain, repair identity |
| 409 | Immutable identity/content conflict | Retain, investigate without overwrite |
| 413 | Body exceeds byte limit | Retain, investigate contract/size |
| 415 | Unsupported media type | Retain, repair sender |
| 503 | Storage not ready or persistence unavailable | Retain and retry |

Authorization middleware may reject requests before the handler executes. A
network cancellation or lost response is uncertain receipt; never synthesize
success. Retrying the exact saved body resolves an already committed receipt.
There is no report-edit, delete, read/list or notification endpoint in this v1
slice. Production activation requires the staging checks in
../pc-pos-central-closing-report-activation.md.
