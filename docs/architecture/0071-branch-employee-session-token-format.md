# ADR 0071: Branch employee session token format

## Status

Accepted.

Defines a bounded, opaque employee-session token envelope and secret verifier
without selecting session lifetime, persistence, or transport.

## Context

ADR 0070 accepts an opaque session token scoped to a trusted Branch and paired
device. A persistence implementation needs a public selector for an indexed
session lookup and a high-entropy secret that can be returned once without
storing the plaintext secret. Employee-session tokens must also be
domain-separated from device credentials so one credential type cannot be
mistaken for the other before lookup.

## Decision

- Use the exact token envelope `es1.<sessionId>.<secret>`, where `es1` identifies
  employee-session format version 1 and is distinct from device credential
  version `v1`.
- Generate `sessionId` from 128 cryptographically strong random bits and the
  secret from 256 cryptographically strong random bits.
- Encode both values as unpadded Base64url. An es1 token is exactly 70 ASCII
  characters: 22 for the identifier, 43 for the secret, three for the version,
  and two separators.
- Store only the public session identifier and the 32-byte SHA-256 hash of the
  random secret. Never store or log the complete token or plaintext secret.
- Verify a presented secret by hashing it and comparing fixed-length hashes
  with `CryptographicOperations.FixedTimeEquals`.
- Reject unknown versions, unexpected lengths, padding, extra separators, and
  characters outside the Base64url alphabet before storage lookup.
- Return a disposable parsed token whose decoded secret buffer is cleared on
  disposal. The original transport string remains owned by the caller and must
  not be logged or retained.
- Handle session-ID collisions through a future unique storage constraint and
  issuance retry.
- Keep expiry and idle-timeout values in durable session state rather than
  embedding timestamps in the token. Their durations remain undecided.
- Do not define session persistence, issuance workflow, revocation, renewal,
  PIN-login transport, cookie or header transport, or ASP.NET authentication in
  this slice.

## Consequences

Future session storage can perform one indexed lookup by session ID, evaluate
current session and employee state, and verify the secret without retaining it.
The format is cryptographically strong and independently versioned, while
session lifetime and revocation can change without changing the token envelope.

The issued token is necessarily secret-bearing and may be exposed only in the
one-time successful login response. Automated tests use generated synthetic
tokens and contact no database, network, or hardware.
