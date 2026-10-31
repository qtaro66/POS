# Branch ordering HTTP API v1

This API exposes the existing durable `OpenTable` and `SubmitOrder` services to
PC POS and Mobile clients. It does not start printer dispatch or Cloud delivery.

All routes below start with `/api/v1/ordering` and require HTTPS and the existing
`BranchPosAuthenticated` device-plus-employee policy. Use the existing device
credential header and `X-POS-Employee-Session` header. No extra PIN is requested
per command. Responses use `Cache-Control: no-store`.

| Method | Relative path | Result |
| --- | --- | --- |
| POST | `/tables/open` | Open or return the existing table session |
| POST | `/orders` | Submit the entire basket as one atomic order batch |
| GET | `/commands/{requestId}` | Query a durable result, including line rejection details |
| GET | `/table-sessions` | List active sessions within the authenticated branch |
| GET | `/table-sessions/{tableSessionId}/menu` | Frozen item/option prices plus current availability |

The endpoints are included in the development OpenAPI document when enabled.
They remain disabled by default. Set `BranchOrdering:Enabled=true` and
`BranchOrdering:DatabasePath` to an existing, initialized Branch database.
`BranchDeviceAuthentication` and `BranchEmployeeSessions` must both be enabled
and point at the same database; their existing readiness/lifetime validations
still apply. Ordering does not create or migrate a database at startup.

## Requests

Open a table:

```json
{
  "schemaVersion": 1,
  "context": {
    "requestId": "OPEN-EXAMPLE-01",
    "branchId": "EXAMPLE-BRANCH",
    "deviceId": "EXAMPLE-DEVICE",
    "employeeId": "EXAMPLE-EMPLOYEE",
    "tableSessionId": "EXAMPLE-SESSION",
    "clientCreatedAt": "2026-10-31T05:00:00Z"
  },
  "tableId": "A01"
}
```

The three identity fields must exactly match authenticated claims; clients
cannot select another branch, device, or employee. Timestamps must be present
and UTC. Identifiers must be normalized, nonblank, contain no control characters,
and fit within 128 characters. JSON bodies are limited to 64 KiB, including
unknown-length/chunked bodies. Unsupported schema versions and unknown JSON
properties are rejected before business writes.

For `POST /orders`, send the same context structure with a new request ID,
the session ID returned by open, and these payload fields:

```json
{
  "lines": [
    {
      "sellableItemId": "EXAMPLE-ITEM",
      "quantity": 1,
      "fulfillmentType": "DineIn",
      "comment": null,
      "options": [
        { "optionGroupId": "EXAMPLE-GROUP", "sellableItemId": "EXAMPLE-OPTION", "quantity": 1 }
      ]
    }
  ]
}
```

The server derives an immutable batch ID from authenticated branch and request
identity; clients do not send an `orderBatchId`.

`fulfillmentType` accepts exactly `DineIn` or `Takeaway`, per line. Options may
be omitted or empty where the catalog permits. Clients never send prices,
totals, VAT, or production destinations. The existing resolver validates the
whole batch and supplies authoritative frozen prices and routing. It continues
to enforce current Sold Out/activation rules and the `PaymentPending` lock.

## Durable results and retry

- HTTP 200: `result.status = BranchAccepted`, returned after the existing
  transaction commits. This does not mean a printer has printed the order.
- HTTP 409: durable business rejection; `result.errorCode` explains it and
  `validationDetails` preserves per-line catalog errors when present.
- HTTP 400: malformed/invalid context or payload, not a durable command result.
- HTTP 401/403: missing authentication or authorization failure.
- HTTP 413: body exceeds the transport limit.
- HTTP 503 with `BranchDatabaseBusy`: transient SQLite contention; preserve the
  request ID and reconcile its durable status before retrying.
- Command query HTTP 404: `NoDurableResult`; not proof of a business rejection.

Use one globally unique request ID per immutable command. Retransmit that exact
command under the same ID after uncertainty; use a new ID for edited content.
This adapter preserves existing branch-scoped deduplication and does not add
payload fingerprinting or change the Application service's receipt semantics.

The command result wrapper contains the existing `BranchCommandStatusResponse`
under `result` and a parsed optional `validationDetails` object. The GET command
route returns HTTP 200 for any existing receipt, including a rejected receipt.

## Read model boundaries

`/table-sessions` lists `Ordering`, `PaymentPending`, `PaymentInProgress`, and
`PartiallyPaid` sessions, scoped to authenticated branch identity. It explicitly
returns `includesAvailableTables=false`. There is no master table-layout registry
yet, so this is not a complete floor plan and it does not invent empty tables.
Open still follows the existing Application rules for caller-supplied table IDs.

Menu queries return the session's frozen `masterDataVersion`, item/option
prices, group constraints, and current availability flags. Previously opened
sessions do not switch prices when a new package activates. Closed sessions and
sessions belonging to another branch return 404. Categories, table layout, sent
order detail, print-job HTTP views, and client UI integration are follow-up work.

## Verification and rollout

Safe tests execute generated minimal-API request delegates, JSON binding/result
serialization, guards, and real SQLite transactions. Authentication uses synthetic
principals; tests inspect the required policy metadata but do not prove real
credential issuance, middleware, TLS, browser/mobile, or hardware operation.
No production database or device is used.

Contracts and security-related activation require D21.3 approval before merge.
