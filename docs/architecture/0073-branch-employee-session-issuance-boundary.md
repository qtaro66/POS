# ADR 0073: Branch employee session issuance boundary

## Status

Accepted.

Defines the application boundary for creating and durably registering an
employee session without selecting a session duration or storage schema.

## Context

After successful device and employee PIN authentication, the Branch must create
a revocable employee session bound to that Branch, device, and employee. The
plaintext token may be returned only after its verifier and lifecycle metadata
have been stored successfully. Session lifetime is still an explicit policy
decision and must not be hidden in this boundary.

## Decision

- Add `BranchEmployeeSessionIssuanceService` using `TimeProvider`, the canonical
  token format from ADR 0071, an issuance store, and an injected lifetime policy.
- Require canonical non-empty Branch, device, and employee identifiers.
- Ask `IBranchEmployeeSessionLifetimePolicy` for an absolute expiry from the UTC
  issuance time. Reject non-UTC or non-future results before token generation or
  storage. This slice defines no duration.
- Add `IBranchEmployeeSessionIssuanceStore.CreateAsync`. Its future
  implementation must atomically insert the session verifier and lifecycle
  metadata or fail without creating a usable session.
- Persist a registration containing the Branch, device, employee, public
  session identifier, UTC issuance and expiry timestamps, and a defensive copy
  of the fixed-length secret hash.
- Return the plaintext token only after the store operation succeeds. Never
  store or log the plaintext token or decoded secret.
- Forward cancellation and propagate policy or storage failures. A failed store
  call must not return a session credential.
- Keep concurrent-session limits, replacement behavior, session-ID collision
  retry, duration values, idle timeout, renewal, revocation workflow, SQLite
  schema, login HTTP transport, and ASP.NET authentication in later slices.

## Consequences

Future login code has one issuance entry point after successful PIN validation,
while deployment-specific session duration remains visible and testable as a
separate policy. Persistence can be added later without exposing plaintext
credentials or changing the token envelope.

Automated tests use a fixed clock, injected lifetime policy, and in-memory store.
They contact no database, network, or hardware.
