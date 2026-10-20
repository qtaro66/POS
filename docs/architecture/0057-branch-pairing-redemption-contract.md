# ADR 0057: Branch pairing redemption contract

## Status

Accepted.

Defines the versioned public request and successful response for redeeming a
Phase 0 pairing challenge, without exposing an HTTP route.

## Context

The Branch runtime can now issue and atomically redeem pairing challenges, but
Mobile and Branch Service need a stable transport shape. The one-time token and
new permanent device credential are sensitive bearer values and must not appear
in unrelated response metadata.

## Decision

- Add a schema-version-1 `BranchPairingRedemptionRequest` containing the Branch
  ID, public pairing ID, one-time token, and Mobile-provided device display name.
- Add a schema-version-1 `BranchPairingRedemptionResponse` containing only the
  newly assigned device ID and permanent device credential.
- Map successful application results only after validating the canonical device
  identifier and permanent credential formats. Dispose parsed credential-secret
  memory immediately after validation.
- Exclude Branch, pairing, employee, approval, expiry, lifecycle, and audit
  fields from the successful response.
- Treat the one-time token in the request and permanent credential in the
  response as secrets: transport code must not log, cache, persist, or include
  either value in telemetry.
- Keep JSON property names governed by ASP.NET's web JSON convention and verify
  the exact public shapes through serialization tests.
- Do not define the endpoint route, authentication scheme, invalid-challenge
  status, model-binding behavior, request-size limit, rate limit, or TLS policy
  in this slice.

## Consequences

The next HTTP-boundary slice can focus on status and security semantics without
changing the Mobile contract. A successful response deliberately returns the
permanent credential once and no internal pairing or employee metadata.

Automated tests use generated synthetic credentials and contact no database,
network, or hardware.
