# ADR 0070: Branch employee session validation boundary

## Status

Accepted.

Defines the application boundary for validating an employee session against a
trusted Branch and paired device without selecting a session token format or
persistence implementation.

## Context

D11 keeps device authentication separate from employee login. A protected POS
request therefore needs both a currently Active device and an independently
validated employee session. The session must not be reusable across a different
Branch or device, and invalid, expired, revoked, inactive-employee, and
ambiguous outcomes must not disclose which check failed.

The employee PIN boundary authenticates a login attempt but does not define the
subsequent session. Session format, issuance, lifetime, persistence, revocation,
attempt throttling, and transport remain unresolved.

## Decision

- Add `IBranchEmployeeSessionValidator.ValidateActiveAsync`, scoped by trusted
  Branch and device identifiers plus a parsed session identifier and decoded
  presented secret.
- Return `VerifiedBranchEmployeeSession` containing canonical Branch, device,
  and employee identifiers only after the session and employee are currently
  valid. Return one null result for invalid, expired, revoked, inactive, or
  ambiguous outcomes.
- Add `BranchEmployeeSessionAuthenticationService` as the application boundary.
  It canonicalizes trusted identifiers and owns bounded token parsing. It
  rejects malformed tokens before validator access and passes only the public
  session identifier and an operation-scoped decoded secret to the validator.
- Reject a validator result whose Branch or device differs from the trusted
  request identity.
- Copy the presented token for parsing, dispose the parsed secret after success
  or failure, and zero the working character buffer. The caller remains
  responsible for its original input buffer.
- Forward cancellation and propagate operational failures rather than treating
  them as invalid credentials.
- ADR 0071 defines token syntax and cryptography. Do not define storage, expiry
  duration, renewal, cookie or header transport, PIN login HTTP contracts,
  dependency injection, an authentication handler, or route activation in this
  slice.

## Consequences

A later persistence implementation has one fail-closed contract for checking a
session against both the authenticated device and the latest locally available
employee status. Future transport code can add the employee claim only after
this boundary succeeds, allowing `BranchPosAuthenticated` to resolve a complete
identity without merging employee login into the device credential.

Automated tests use an in-memory validator and contact no database, network, or
hardware.
