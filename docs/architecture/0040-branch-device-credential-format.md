# ADR 0040: Branch device credential format and hash verification

## Status

Accepted.

Defines the bounded Phase 0 credential envelope and secret verification
primitive used by the D11 validation boundary.

## Context

ADR 0039 deliberately left credential format and hashing unresolved. The Branch
needs a public selector for indexed lookup and a high-entropy secret that can be
shown to the Mobile once without storing that secret in plaintext. Parsing must
be bounded before later authentication code accesses persistence.

Device credentials have no fixed expiry in Phase 0. Their validity is governed
by the current durable device and credential state, not a timestamp embedded in
the token.

## Decision

- Use the exact token envelope `v1.<credentialId>.<secret>`.
- Generate `credentialId` from 128 cryptographically strong random bits and the
  secret from 256 cryptographically strong random bits.
- Encode both values as unpadded Base64url. A v1 token is exactly 69 ASCII
  characters: 22 for the identifier and 43 for the secret plus separators and
  version.
- Store only the public credential identifier and the 32-byte SHA-256 hash of
  the random 256-bit secret. Never store or log the complete token or plaintext
  secret.
- Verify a presented secret by hashing it and comparing the two fixed-length
  hashes with `CryptographicOperations.FixedTimeEquals`.
- Reject unknown versions, unexpected lengths, padding, extra separators, and
  characters outside the Base64url alphabet before storage lookup.
- Return a disposable parsed credential whose decoded secret buffer is cleared
  on disposal. The original transport string remains owned by the transport
  layer and must not be logged or retained.
- Credential-ID collisions must be handled by a unique storage constraint and
  issuance retry in the future persistence workflow.
- Keep device records, credential lifecycle state, rotation, issuance audit,
  and ASP.NET authentication composition in later slices.

## Consequences

The future SQLite validator can perform one indexed lookup by credential ID,
check the current device and credential state, and use the shared fixed-time
verification primitive. Database theft does not reveal the random secret
directly, while credential revocation remains independent of token age.

The issued token is necessarily secret-bearing because Mobile must store it.
Only the one-time issuance response may expose it. Automated tests use generated
synthetic credentials and contact no database, network, or hardware.
