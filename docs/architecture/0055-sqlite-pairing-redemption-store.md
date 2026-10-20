# ADR 0055: SQLite pairing redemption store

## Status

Accepted.

Implements the atomic SQLite transaction that redeems one active Branch pairing
challenge into a durable device and permanent credential.

## Context

ADR 0054 requires challenge validation, device creation, credential creation,
and single-use consumption to succeed or roll back together. The version-12
Branch schema already contains the guarded pairing, device, and credential
tables needed for this transaction.

## Decision

- Add `SqliteBranchPairingRedemptionStore` using a private, non-pooled,
  foreign-key-enabled read-write connection and `BEGIN IMMEDIATE`.
- Select a challenge by exact Branch and pairing ID inside the transaction. Use
  one null/false outcome for missing, non-Active, expired, wrong-secret, and
  cross-Branch attempts.
- Parse stored expiry only as canonical round-trip UTC and require
  `redeemedAt < expiresAt`. Treat malformed durable timestamps as corruption,
  not as an invalid credential.
- Verify the stored pairing hash against the presented 32-byte secret through
  the existing fixed-time primitive.
- Read the pairing approver from immutable `created_by_employee_id`; never take
  that identity from Mobile input.
- Insert the Active device and Active credential, then conditionally update the
  still-Active challenge to `Consumed` with the same UTC timestamp. Commit only
  after all three writes succeed.
- Propagate uniqueness, foreign-key, cancellation, corruption, and operational
  errors so transaction disposal rolls back every partial write.
- Copy and zero the presented pairing secret, stored pairing hash, and permanent
  credential hash around the awaited transaction. Persist no plaintext secret.
- Reuse schema version 12; no migration or new table is required.
- Do not compose the store, expose a route, define HTTP redemption semantics,
  retry identifier collisions, rotate credentials, or add revocation commands
  in this slice.

## Consequences

SQLite now enforces all-or-nothing local pairing while the Branch remains
offline from Cloud. A consumed or rejected token cannot create another device,
and an insert conflict leaves the challenge Active for a safe retry with a new
generated registration.

Automated tests use isolated temporary databases and synthetic secrets and
contact no network or hardware.
