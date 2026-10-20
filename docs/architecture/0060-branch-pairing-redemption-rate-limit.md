# ADR 0060: Branch pairing redemption rate limit

## Status

Accepted.

Adds a bounded per-client attempt policy to the still-inactive Phase 0 pairing
redemption endpoint.

## Context

The redemption route is intentionally anonymous at the ASP.NET authorization
layer because the one-time token is its credential. Even though the token has
high entropy and a five-minute lifetime, an unbounded endpoint could consume
Branch CPU, database locks, and connection capacity under repeated invalid
requests.

## Decision

- Register one named ASP.NET Core partitioned rate-limit policy when Branch
  pairing is enabled and attach that policy name to the redemption endpoint.
- Partition by the direct connection `RemoteIpAddress`; do not trust forwarded
  headers until a reviewed trusted-proxy configuration exists.
- Permit 10 attempts per fixed one-minute window for each client IP.
- Queue no excess requests. The rate-limiter middleware can reject them
  immediately rather than extending load or token lifetime.
- Put requests without a remote IP into one shared `unknown-client` partition so
  they remain bounded instead of bypassing the limiter.
- Return `429 Too Many Requests` through the framework with no custom rejection
  callback. Do not include pairing or credential values in a rejection body,
  log, or metric label.
- Keep the endpoint inactive. A later activation slice must register and place
  the rate-limiter middleware before mapping the endpoint and must verify the
  externally observed rejection status.
- Do not add forwarded-header trust, distributed counters, persistence, dynamic
  configuration, request-body limits, or endpoint activation in this slice.

## Consequences

Every future mapped redemption endpoint carries a testable per-client policy and
cannot accidentally be activated without rate-limit metadata. Multiple Branch
processes would enforce independent counters; Phase 0 uses one Branch Service
process, while multi-instance deployment requires a later design review.

Automated tests instantiate in-memory limiters and contact no database, network,
or hardware.
