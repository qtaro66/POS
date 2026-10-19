# ADR 0049: SQLite pairing token issuance store

## Status

Accepted.

Persists Branch pairing challenges and atomically replaces the creator's earlier
active challenge.

## Context

ADR 0048 requires hash-only local persistence and one active pairing challenge
per Branch and creator. Replacing the old challenge and inserting the new one
must be a single durable transaction so a failed insertion cannot leave the
creator without the still-valid previous challenge.

Pairing must operate while Cloud connectivity is unavailable. The Branch API is
the only process allowed to write the Branch SQLite database.

## Decision

- Advance the Branch schema to version 12 and add `branch_pairing_tokens`.
- Store the canonical pairing ID, Branch and creator identifiers, a 32-byte hash,
  UTC issuance and expiry timestamps, and lifecycle metadata. Never store the
  plaintext token.
- Permit `Active`, `Replaced`, and future `Consumed` lifecycle states. Preserve
  terminal history with database triggers and make identity, hash, and original
  timestamps immutable.
- Enforce at most one `Active` challenge per Branch and creator with a partial
  unique index.
- Implement `SqliteBranchPairingTokenIssuanceStore` with a non-deferred write
  transaction. Mark the previous active record `Replaced`, link it to the new
  pairing ID, insert the replacement, and commit as one unit.
- Roll back the replacement when insertion or commit fails. Use read-write mode
  without create so a missing database fails closed.
- Copy the registration hash for the SQLite parameter and zero that working copy
  after success or failure.
- Do not implement redemption, device creation, permanent credential issuance,
  audit events, dependency-injection composition, or HTTP exposure in this
  slice.

## Consequences

Offline QR issuance now has a durable atomic store and retains replacement
history. A later transaction can validate an unexpired Active challenge and
atomically create the Branch device and permanent credential before marking the
challenge `Consumed`.

Automated tests use temporary databases only and contact no network or hardware.
