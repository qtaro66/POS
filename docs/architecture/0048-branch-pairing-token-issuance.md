# ADR 0048: Branch pairing token issuance

## Status

Accepted.

Defines the application boundary for issuing a five-minute QR pairing challenge
and replacing the creator's earlier active challenge.

## Context

D11 requires each authenticated POS user to have at most one active pairing
token, with a newly generated QR invalidating that user's earlier unused token.
The token must expire after five minutes, retain only a hash in Branch storage,
and remain issuable while Cloud connectivity is unavailable.

Issuance can be defined independently from redemption. Redemption must later be
atomic with device creation and permanent credential issuance; consuming a token
in an earlier transaction could strand a valid pairing attempt without a device.

## Decision

- Add `BranchPairingTokenIssuanceService` using `TimeProvider` and the canonical
  format from ADR 0047.
- Fix the challenge lifetime at five minutes and persist UTC issuance and expiry
  timestamps.
- Scope each registration by canonical Branch ID and authenticated creator
  employee ID.
- Add `IBranchPairingTokenIssuanceStore.ReplaceActiveForCreatorAsync`. Its
  implementation must atomically invalidate any earlier active token for the
  same Branch and creator while inserting the new hash and lifecycle metadata.
- Return the plaintext token only after the store operation succeeds. Persist
  only a defensive copy of the fixed-length secret hash.
- Forward cancellation and propagate storage failures. Do not return a challenge
  when persistence fails.
- Do not implement SQLite storage, audit, QR URL/payload rendering, redemption,
  device creation, permanent credential issuance, or HTTP exposure in this
  slice.

## Consequences

The future SQLite store has one explicit atomic responsibility and can support
offline Branch issuance. A later redemption transaction must validate expiry and
single-use state, create the device and credential, mark the challenge consumed,
and append audit data atomically.

Automated tests use a fixed clock and in-memory store and contact no database,
network, or hardware.
