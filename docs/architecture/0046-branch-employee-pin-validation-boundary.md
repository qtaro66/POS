# ADR 0046: Branch employee PIN validation boundary

## Status

Accepted.

Defines the application boundary for authenticating an Active employee from the
latest employee and PIN-verifier data available at the Branch.

## Context

D11 requires employee login to continue while Cloud connectivity is unavailable
and requires central revocation to take effect when the updated employee data
reaches the Branch. Device authentication remains separate from employee login,
so a paired device alone cannot produce a complete POS request identity.

The PIN format, duplicate-PIN policy, attempt throttling, employee-session
lifetime, persistence schema, and HTTP login contract are not yet fixed. This
slice must not decide those policies implicitly.

## Decision

- Add `IBranchEmployeePinValidator.ValidateActiveAsync`, scoped by the trusted
  Branch identifier and a presented PIN.
- Return `VerifiedBranchEmployee` containing only canonical Branch and employee
  identifiers when exactly one Active employee matches the authoritative local
  verifier. Return one null result for an invalid, inactive, or ambiguous match.
- Reject a validator result whose Branch does not exactly match the trusted
  Branch requested by the authentication boundary.
- Add `BranchEmployeePinAuthenticationService` as the application boundary. It
  canonicalizes the Branch identifier but passes PIN characters exactly as
  presented, without trimming, normalizing, logging, or converting them to a
  managed string.
- Copy the presented PIN for validator use and zero that working buffer after
  success or failure. The caller remains responsible for the lifetime of its
  original input buffer.
- Forward cancellation and propagate operational failures. Do not recast them
  as invalid credentials.
- Do not define PIN syntax, hashing parameters, duplicate resolution, rate
  limiting, lockout, session tokens, permissions, persistence, dependency
  injection, an authentication handler, middleware, or an HTTP endpoint in this
  slice.

## Consequences

A later SQLite implementation can validate the most recently synchronized
employee verifier without coupling application code to SQLite. A separate login
boundary must apply attempt throttling and issue a revocable employee session
before any protected POS route can resolve a complete
`AuthenticatedBranchRequestIdentity`.

Automated tests use an in-memory validator and contact no database, network, or
hardware.
