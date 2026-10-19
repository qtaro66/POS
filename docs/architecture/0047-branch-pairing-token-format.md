# ADR 0047: Branch pairing token format

## Status

Accepted.

Defines the canonical public identifier and one-time secret format used by the
QR device-pairing workflow without implementing persistence or redemption.

## Context

D11 requires each QR pairing attempt to contain a `pairingId` and a random
one-time token, use cryptographically secure randomness of at least 128 bits,
expire after five minutes, work on the Branch LAN while Cloud connectivity is
unavailable, and store only a hash after the QR is displayed.

The format needs a strict boundary before durable single-use redemption is
implemented. Expiry, creator ownership, replacement of an earlier active token,
audit, and atomic redemption belong to persistence and application services,
not the token encoding itself.

## Decision

- Generate a 128-bit random `pairingId`, encoded as canonical unpadded Base64url.
- Generate a separate 256-bit random secret and expose the one-time token as
  `v1.<base64url-secret>`.
- Return the plaintext token only in the issuance result and return a defensive
  copy of its SHA-256 hash for persistence. The future store must never persist
  the plaintext token.
- Parse exact, case-sensitive, fixed-length canonical version-1 tokens only.
  Reject padding, aliases, extra separators, whitespace, and unknown versions.
- Keep parsed secret bytes disposable and zero them on disposal. Zero temporary
  generation and parsing buffers.
- Compare a presented secret with a stored hash in fixed time after enforcing
  exact lengths.
- Do not embed Branch identity, employee identity, expiry, URL, device data, or
  a permanent device credential in the token.
- Do not implement the five-minute expiry, one-active-token-per-creator rule,
  single-use state transition, persistence, QR payload, audit, device creation,
  or HTTP endpoint in this slice.

## Consequences

The future pairing store can key records by the public `pairingId`, retain only
the hash and lifecycle metadata, and atomically consume a canonical presented
token. The token contains 256 bits of secret randomness, exceeding D11's
minimum, while the public identifier remains separate for indexed lookup.

Automated tests use generated synthetic values and contact no database, network,
or hardware.
