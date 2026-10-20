# ADR 0063: Branch pairing rate-limiter middleware

## Status

Accepted.

Composes the already-defined pairing redemption rate-limit policy into the
Branch HTTP pipeline without activating either pairing endpoint.

## Context

The redemption endpoint metadata requires the named
`BranchPairingRedemption` policy, but endpoint metadata is inert unless the
ASP.NET Core rate-limiter middleware is present after routing. Pairing is still
disabled by default, so its middleware must not introduce a runtime dependency
when that feature is off.

## Decision

- Add explicit routing before the pairing rate-limiter middleware.
- Add the middleware only when `BranchPairing:Enabled` is true. The same flag
  already controls registration of the named policy and its runtime services.
- Leave the redemption and issuance endpoints unmapped.
- Preserve the existing fixed-window policy, per-IP partition, rejection
  status, and zero-queue behavior.
- Do not change HTTPS listeners, certificates, request limits, endpoint
  contracts, authentication, or authorization in this slice.

## Consequences

An enabled pairing runtime now has the middleware required to enforce endpoint
rate-limit metadata once the route is deliberately activated. A disabled
runtime keeps the current pipeline and needs no pairing rate-limiter services.

Automated tests build and invoke in-memory middleware pipelines only. They
contact no network, database, or hardware.
