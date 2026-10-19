# ADR 0041: Branch device token authentication composition

## Status

Accepted.

Connects the bounded v1 token format from ADR 0040 to the device credential
validation boundary from ADR 0039.

## Context

The transport-facing authentication code will receive one opaque credential
token, while the validator requires a public credential identifier and decoded
secret bytes. Parsing and secret-buffer ownership must be centralized so later
ASP.NET authentication code cannot accidentally bypass the canonical format or
retain decoded secret material.

## Decision

- Add `BranchDeviceTokenAuthenticationService` as the only application service
  that accepts the complete device token.
- Parse with `BranchDeviceCredentialFormat.TryParse` before any persistence
  lookup. Malformed, unsupported, padded, noncanonical, or otherwise invalid
  tokens return the same unauthenticated result and do not call the validator.
- Pass only the parsed credential identifier and decoded secret to
  `BranchDeviceCredentialAuthenticationService`.
- Dispose the parsed credential after success, inactive-credential rejection,
  cancellation, or validator failure so its decoded secret buffer is cleared.
- Preserve the validation boundary's operation-scoped secret copy and clearing.
  The validator never receives memory owned by the token parser.
- Propagate infrastructure failures and cancellation. Only invalid token or
  inactive credential outcomes are mapped to the common unauthenticated result.
- Do not log, persist, or return the complete token or decoded secret.
- Keep SQLite validation, device lifecycle writes, rotation, issuance,
  employee-session validation, and ASP.NET authentication registration in later
  slices.

## Consequences

Future transport code has one strict entry point and cannot use token-controlled
data until canonical parsing succeeds. Invalid tokens and inactive credentials
remain indistinguishable at this boundary, while operational failures remain
visible instead of being silently treated as invalid credentials.

The original token is an immutable transport string and cannot be cleared by
this service. Its owner must avoid logging or retaining it. Automated tests use
only generated synthetic credentials and contact no database, network, or
hardware.
