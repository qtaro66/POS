# ADR 0042: SQLite branch device credential validation

## Status

Accepted.

Adds the first durable D11 device and credential records and a read-only
validator for every protected Mobile request.

## Context

ADR 0039 defines the validation boundary, ADR 0040 defines the credential
format and hash verification, and ADR 0041 composes token parsing with that
boundary. The Branch still needs an authoritative local lookup that remains
available when the Cloud is unreachable and rejects a credential immediately
after its durable Branch state changes.

## Decision

- Add schema version 11 with separate `branch_devices` and
  `branch_device_credentials` tables.
- Store only the public 22-character credential identifier and the 32-byte
  SHA-256 secret hash. The complete token and plaintext secret are never stored.
- Require device state `Active`, `Revoked`, or `Lost`; require credential state
  `Active`, `Revoked`, `Lost`, or `Replaced`.
- Allow at most one Active credential per device. Credential identifiers are
  globally unique selectors.
- Defer the self-reference from a Replaced credential to its replacement until
  transaction commit. Rotation can retire the old row before inserting the new
  Active row without opening a two-credential window.
- Make terminal device and credential records immutable. Pairing a previously
  retired Mobile creates a new credential record instead of reactivating or
  rewriting the old one.
- Prevent deletion of device and credential rows so retired history remains
  available to later audit and investigation workflows.
- Make credential identity, device ownership, secret hash, and issuance time
  immutable after insertion.
- Add `SqliteBranchDeviceCredentialValidator` using a read-only SQLite
  connection. It joins the credential to its device and returns identity only
  when both records are currently Active and the stored hash matches through
  the fixed-time verifier from ADR 0040.
- Return the same unauthenticated result for missing records, inactive devices,
  inactive credentials, wrong secrets, and unexpected secret lengths.
- Do not update `last_seen_at` during validation. Per-request authentication is
  read-only; a later bounded activity service may update operational metadata
  without changing authentication correctness.
- Keep issuance, pairing, revocation commands and audit events, transparent
  rotation workflow, employee sessions, and ASP.NET authentication registration
  in later slices.

## Consequences

The Branch database is now authoritative for device authentication while
offline. A durable transition away from Active takes effect on the next
validation call without an in-memory authorization cache. The validator cannot
create a missing database or mutate device state.

The schema includes the lifecycle fields needed by later command workflows but
does not expose those writes yet. Automated tests use temporary databases and
generated synthetic credentials; they contact no network or hardware.
