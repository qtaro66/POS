# ADR 0037: Branch command status HTTP semantics

## Status

Accepted.

Defines the HTTP result boundary for D20.1 recovery without registering a
public route.

## Context

ADR 0036 defines a transport-safe response contract, but an endpoint also needs
unambiguous HTTP behavior. A missing receipt must not look like an accepted or
rejected command, and a database or parsing failure must not be converted into a
normal missing-result response.

The Branch identity is security-sensitive. A future route must derive it from
the authenticated device context rather than accept a branch identifier from a
client-controlled route, query string, or body.

## Decision

- Add an HTTP handler boundary that receives an `authenticatedBranchId`, the
  original `requestId`, and the authoritative query.
- Canonicalize non-empty identifiers before the query and response mapper use
  them.
- Return HTTP `200 OK` with the versioned response for durable
  `BranchAccepted` or `Rejected` receipts.
- Return HTTP `404 Not Found` with `NoDurableResult` when no durable receipt
  exists. This does not authorize a new request identifier; D12.2 still
  requires any resend to reuse the original `requestId`.
- Forward request cancellation to the query.
- Propagate storage, parsing, and identity mismatch failures. Do not recast them
  as `404`, `Rejected`, or a successful response.
- Do not register a route in this slice. Route exposure remains blocked until
  device authentication and authorization can supply the trusted Branch
  identity.

## Consequences

The future authenticated endpoint can remain a thin route adapter with tested
status semantics. It must not bind `branchId` from Mobile input. This slice
starts no listener or background service and performs no network, database, or
hardware access in automated tests.
