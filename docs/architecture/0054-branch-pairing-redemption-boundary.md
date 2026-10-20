# ADR 0054: Branch pairing redemption boundary

## Status

Accepted.

Defines the application boundary for atomically redeeming a one-time Branch
pairing challenge into a new device and permanent revocable credential.

## Context

D11 requires a pairing token to work on the Branch LAN while Cloud connectivity
is unavailable, expire after five minutes, and succeed only once. Successful
pairing must create a device record and issue a revocable device credential.
Consuming the challenge before both durable records exist could strand the
Mobile without a usable credential.

SQLite transaction details and the public redemption HTTP contract remain
separate concerns.

## Decision

- Add a canonical 128-bit random, unpadded Base64url device identifier format.
- Add `BranchPairingRedemptionService`, which accepts the trusted Branch ID,
  canonical public pairing ID, presented one-time token, and device display
  name.
- Treat malformed pairing IDs and tokens as one null redemption result without
  storage access. Do not expose whether a challenge is missing, replaced,
  expired, consumed, belongs to another Branch, or has the wrong secret.
- Generate a new device ID and permanent v1 device credential for each attempt.
  Pass only the parsed pairing secret, credential hash, and durable registration
  metadata to the store. Return the plaintext permanent credential only after
  the store reports a committed success.
- Add `IBranchPairingRedemptionStore.TryRedeemAsync`. Its future implementation
  must use one transaction to validate Branch and pairing identity, Active
  status, strict expiry (`redeemedAt < expiresAt`), and the pairing secret in
  fixed time; insert the Active device and credential; mark the challenge
  `Consumed`; and commit all changes together.
- The store reads `created_by_employee_id` from the challenge as the device's
  pairing approver. The Mobile cannot supply or replace that identity.
- Keep the parsed pairing-secret buffer alive only for the awaited store call
  and zero it immediately afterward. The store must not retain that memory after
  its task completes. Persist only the permanent credential secret hash, never
  either plaintext token.
- Forward cancellation and propagate operational failures. Identifier collisions
  are operational failures in this slice; bounded collision retry requires a
  later service/store boundary revision.
- Do not implement SQLite redemption, an HTTP route, QR scanning, device-name
  policy beyond non-empty normalization, credential delivery storage, employee
  sessions, or revocation commands in this slice.

## Consequences

The future SQLite adapter has one atomic responsibility and cannot consume a
challenge without also creating the complete device credential state. Invalid
or unavailable challenges share one public outcome, reducing status-oracle
leakage. The permanent credential is necessarily returned once to Mobile and
must not be logged, cached, or persisted outside protected client storage.

Automated tests use generated synthetic secrets, an in-memory store, and a fixed
clock and contact no database, network, or hardware.
