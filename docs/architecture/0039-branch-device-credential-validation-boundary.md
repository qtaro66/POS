# ADR 0039: Branch device credential validation boundary

## Status

Accepted.

Defines the secret-handling and validation contract for D11 device
authentication without selecting a credential format or persistence schema.

## Context

D11 requires every protected request to check the Mobile device's current
`Active` or `Revoked` state. `Revoked` and `Lost` credentials must not
authenticate, credentials have no fixed expiry in Phase 0, and credential
rotation must remain possible. Device authentication remains separate from
employee login.

The Application layer needs a platform-independent validation boundary before
the Branch API can implement an authentication scheme. Credential secrets must
not be logged, cached in options, or retained after a validation attempt.

## Decision

- Add `IBranchDeviceCredentialValidator.ValidateActiveAsync`, keyed by a public
  credential identifier and an opaque secret byte sequence.
- Return a `VerifiedBranchDevice` containing only Branch and device identifiers
  when the credential matches a device that is currently `Active`.
- Return one `null` result for invalid, revoked, lost, replaced, or otherwise
  inactive credentials. Do not disclose the reason at the authentication
  boundary.
- Validate current durable device state on every call; implementations must not
  treat an earlier successful result as permanent authorization.
- Copy the presented secret into an operation-scoped working buffer and clear
  that buffer after success, rejection, cancellation, or failure.
- Do not modify the caller-owned secret buffer. Its transport owner remains
  responsible for clearing or releasing the original representation.
- Require non-empty canonical credential, Branch, and device identifiers.
- Keep credential token format, secret hashing, constant-time comparison,
  durable storage, rotation response, and ASP.NET authentication composition in
  later slices.

## Consequences

The persistence adapter can enforce `Active` state and constant-time secret
verification without exposing storage details to the API. The future
authentication scheme can issue branch and device claims only after this
boundary returns a verified device, then independently validate the employee
session before producing the complete request identity from ADR 0038.

Automated tests use synthetic byte arrays only and verify that the
operation-scoped copy is cleared. They contact no database, network, or
hardware.
