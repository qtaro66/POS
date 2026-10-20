# ADR 0066: Branch pairing redemption runtime activation

## Status

Accepted.

Activates the anonymous pairing redemption route only when the pairing runtime
is explicitly enabled.

## Context

The redemption boundary now has a one-time token store, stable contract,
uniform failure response, strict request-size limit, per-IP rate limit,
plaintext-request guard, and cache prevention for the returned permanent device
credential. The route metadata remained inactive while those controls were
assembled.

QR challenge issuance still requires a complete employee-session
authentication boundary and therefore must remain inactive.

## Decision

- Map `POST /api/v1/pairing/redemptions` only when
  `BranchPairing:Enabled` is true.
- Keep pairing disabled by default.
- Reuse the existing anonymous route metadata, 4 KiB request limit, named
  fixed-window rate-limit policy, HTTPS-only namespace guard, and no-store
  response headers without changing their semantics.
- Keep challenge issuance unmapped.
- Do not add HTTP fallback, certificate-validation bypasses, request or response
  body logging, OpenAPI exposure, or secret telemetry.

## Consequences

An operator who supplies valid pairing database and service-origin
configuration and deliberately enables pairing can expose redemption through
the Branch runtime. Plaintext requests still fail before route execution, and
clients must trust the configured Private CA and IP-SAN certificate.

Issuance remains unavailable until employee authentication and its authorization
rule are safely composed. Deployment still owns Kestrel HTTPS binding,
certificate installation and renewal, IP SAN correctness, and firewall rules.

Automated tests inspect endpoint data sources in memory only. They contact no
network, database, or hardware.
