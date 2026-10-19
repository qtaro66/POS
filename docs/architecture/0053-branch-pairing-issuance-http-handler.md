# ADR 0053: Branch pairing issuance HTTP handler

## Status

Accepted.

Defines a thin, testable HTTP handler for issuing a Branch pairing challenge
from a complete authenticated request identity without exposing a route.

## Context

D11 permits every successfully authenticated POS employee to create a pairing
QR without a separate role or repeated PIN prompt. Branch and creator identity
must not come from request parameters, and an invalid public Branch Service URL
must not create an active token that cannot be returned to the caller.

The issuance service, SQLite store, response contract, and QR payload format are
already bounded. Employee-session authentication and the final authorization
policy are not yet implemented, so routing remains unsafe.

## Decision

- Add `BranchPairingIssuanceHttpHandler` accepting only an
  `AuthenticatedBranchRequestIdentity`, configured Phase 0 Branch Service URL,
  composed issuance service, and request cancellation token.
- Validate the Branch Service URL before persistence so invalid configuration
  cannot replace the creator's active token.
- Derive both Branch ID and creator employee ID exclusively from the
  authenticated identity. Do not accept either as route, query, or body input.
- Issue the challenge through the application service, preserving its atomic
  replacement and immutable creator/timestamp history, then map it to the
  versioned public response.
- Return HTTP 200 for successful issuance. Propagate cancellation and
  operational persistence failures instead of returning a challenge that was
  not durably stored.
- Do not map a route, add authorization policy, define employee sessions,
  generate QR images, log the response, redeem tokens, or issue device
  credentials in this slice.

## Consequences

A future route can remain a thin adapter once complete employee authentication
and authorization are available. Until then, the handler is not externally
reachable. The returned response contains the one-time token and must never be
logged, cached, or persisted by future transport middleware or UI code.

Automated tests use an in-memory issuance store and fixed clock and contact no
network, database, or hardware.
