# ADR 0056: Pairing redemption composition

## Status

Accepted.

Composes the pairing-redemption application service and SQLite store in the
Branch API runtime when Branch pairing is enabled.

## Context

ADR 0055 provides the atomic SQLite adapter, but the Branch runtime does not yet
resolve it through dependency injection. Pairing issuance already uses the same
validated database path and feature switch.

## Decision

- When `BranchPairing:Enabled` is true, register one singleton
  `IBranchPairingRedemptionStore` backed by
  `SqliteBranchPairingRedemptionStore` using the validated, trimmed
  `BranchPairing:DatabasePath`.
- Register one singleton `BranchPairingRedemptionService` using the shared
  `TimeProvider` already composed for pairing issuance.
- Keep both redemption registrations absent when Branch pairing is disabled.
- Continue to fail startup through the existing options validator when the
  database path, schema version, or required pairing tables are unavailable.
- Verify the composition through an isolated schema-version-12 database by
  issuing and redeeming one challenge and checking the committed device,
  credential, and consumed challenge state.
- Do not add an HTTP route, request or response contract, endpoint authorization,
  QR scanning, identifier-collision retry, or credential revocation in this
  slice.

## Consequences

The Branch runtime can now resolve the complete pairing-redemption use case,
while no network endpoint exposes it yet. Issuance and redemption use separate
store instances over the same validated SQLite database and share the same
runtime clock.

Automated tests use an isolated temporary database and contact no network or
hardware.
