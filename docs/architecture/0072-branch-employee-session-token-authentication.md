# ADR 0072: Branch employee session token authentication

## Status

Accepted.

Connects the canonical employee-session token format to the session-validation
boundary without adding storage or HTTP authentication.

## Context

ADR 0070 established a fail-closed employee-session boundary, and ADR 0071
defined the bounded `es1` token format. Parsing must occur before any persistence
lookup so malformed or unsupported tokens cannot reach a validator, while
decoded secret material must have one clear owner and lifetime.

## Decision

- Make `BranchEmployeeSessionAuthenticationService` the only application
  service that accepts a complete employee-session token.
- Parse the token with `BranchEmployeeSessionTokenFormat` before calling the
  validator. Malformed, unsupported, padded, noncanonical, or otherwise invalid
  tokens return the same unauthenticated result and perform no validator call.
- Pass only the trusted Branch and device identifiers, parsed public session
  identifier, and decoded secret to `IBranchEmployeeSessionValidator`.
- Add span-based parsing so the operation-scoped token character buffer does
  not need to become another managed string. Only the non-secret session
  identifier is materialized.
- Dispose the parsed token after success, unauthenticated result, cancellation,
  mismatch, or validator failure so decoded secret bytes are cleared. Also zero
  the service's operation-scoped character copy.
- Keep invalid token and invalid, expired, revoked, inactive, or ambiguous
  session outcomes indistinguishable. Propagate operational failures and
  cancellation.
- Do not log, persist, or return the complete token or decoded secret.
- Do not define SQLite validation, session issuance or lifetime, renewal,
  revocation workflow, HTTP transport, dependency injection, an authentication
  handler, or route activation in this slice.

## Consequences

Future persistence code receives only canonical lookup material and cannot be
invoked for malformed credentials. Secret-buffer ownership is centralized, and
transport code will have one authentication entry point rather than parsing or
validating tokens itself.

Automated tests use generated synthetic tokens and an in-memory validator. They
contact no database, network, or hardware.
