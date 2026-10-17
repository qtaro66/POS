# ADR 0036: Branch command status response contract

## Status

Accepted.

Defines the transport-safe response mapping for the D20.1 recovery query
without exposing an HTTP endpoint.

## Context

The authoritative query returns an internal `BranchCommandReceipt`. That record
also contains Branch, device, employee, and diagnostic fields that a Mobile
status response does not need. Returning the persistence model directly would
couple the public API to internal storage and could disclose unnecessary data.

A missing durable receipt is not evidence that the command was rejected. It
means only that the Branch has no durable accepted or rejected result for the
requested identity at query time.

## Decision

- Define a versioned `BranchCommandStatusResponse` contract with stable string
  status values: `BranchAccepted`, `Rejected`, and `NoDurableResult`.
- Correlate every response with the original `requestId`.
- For a durable receipt, expose only the table-session recovery snapshot,
  public error code, Branch receive time, and Branch sequence.
- Do not expose `branchId`, `deviceId`, `employeeId`, client time, or internal
  error-details JSON in the response contract.
- Map a missing receipt to `NoDurableResult`, never to `Rejected` or
  `BranchAccepted`. The client must retain the original request identity.
- Fail closed if a receipt does not exactly match the requested branch and
  request identifiers.
- Keep HTTP status codes, authentication, authorization, and route exposure for
  the later endpoint slice.

## Consequences

The future Mobile endpoint can return a stable, minimal response without
serializing the persistence model. `NoDurableResult` gives the transport layer
an explicit safe result to map while preserving the D12.2 rule that any resend
uses the same `requestId`.

This contract does not itself authorize access, query storage, or start network
activity. Automated tests serialize only synthetic values and contact no
external service or hardware.
