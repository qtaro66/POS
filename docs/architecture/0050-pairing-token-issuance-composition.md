# ADR 0050: Pairing token issuance composition

## Status

Accepted.

Composes the Branch pairing challenge issuance boundary behind disabled-by-default
configuration without exposing an HTTP route.

## Context

ADR 0049 adds the version-12 SQLite issuance store. Runtime composition must
remain inert until an operator supplies an existing migrated Branch database and
explicitly enables pairing. Startup must fail before serving requests when the
configured database is missing, unreadable, stale, or lacks the pairing table.

## Decision

- Add the `BranchPairing` configuration section with `Enabled` and
  `DatabasePath`; keep it disabled with an empty path by default.
- When disabled, require no database and register no issuance store or service.
- When enabled, require an absolute existing database path, exact current schema
  version, and the `branch_pairing_tokens` table through a read-only readiness
  probe.
- Compose one singleton `SqliteBranchPairingTokenIssuanceStore`, one singleton
  `BranchPairingTokenIssuanceService`, and `TimeProvider.System` unless a host has
  already supplied a clock.
- Register composition from the Branch API startup, but do not add hosted work,
  middleware, authorization changes, QR rendering, or an HTTP route.

## Consequences

Operators can enable offline pairing issuance only against a migrated database,
and configuration errors fail closed during startup validation. The application
still has no externally reachable pairing endpoint; a later route must require
the correct authenticated employee boundary and record audit data.

Automated tests use temporary databases and contact no network or hardware.
