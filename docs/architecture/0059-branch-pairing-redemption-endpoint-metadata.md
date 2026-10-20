# ADR 0059: Branch pairing redemption endpoint metadata

## Status

Accepted.

Defines the exact Phase 0 redemption route and its security intent without
activating it in the Branch application.

## Context

The redemption handler has stable HTTP results, but mapping it directly in
`Program.cs` would expose a bearer-token endpoint before rate limiting,
request-size limits, and final production HTTPS configuration are attached.
The route still needs an explicit identity and authorization model so later
composition cannot accidentally place it behind an already-paired device policy.

## Decision

- Define one `POST /api/v1/pairing/redemptions` minimal-API endpoint named
  `RedeemBranchPairing`.
- Mark the endpoint explicitly anonymous at the ASP.NET authorization layer.
  The one-time pairing token is the endpoint credential; a permanent device
  credential cannot exist before successful redemption.
- Attach no authenticated-device or employee policy to this endpoint.
- Exclude the endpoint from generated OpenAPI while it is not production-ready,
  reducing accidental discovery and preventing an incomplete public API promise.
- Keep mapping in a dedicated extension and do not call it from `Program.cs` in
  this slice.
- Before activation, add per-client rate limiting, a small request-body limit,
  strict production HTTPS/certificate configuration, and integration coverage
  proving that sensitive request and response values are not logged.

## Consequences

The route name, method, path, and anonymous pre-pairing intent are now testable
without making the endpoint reachable. A later activation change remains small
and reviewable but must not call this mapper until the listed protections exist.

Automated tests inspect endpoint metadata in memory and contact no database,
network, or hardware.
