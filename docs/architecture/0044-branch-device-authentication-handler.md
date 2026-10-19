# ADR 0044: Branch device authentication handler

## Status

Accepted.

Adds the ASP.NET authentication boundary for a paired Mobile device without
granting employee authority or exposing a protected route.

## Context

ADR 0043 composes the token-authentication service but does not provide an HTTP
credential boundary. Later protected Mobile routes need a standard ASP.NET
authentication result whose claims come only from the durable validator, never
from request-controlled Branch or device fields.

Device authentication remains separate from employee login. A valid paired
device alone must not satisfy `AuthenticatedBranchRequestIdentityResolver`,
which also requires one authenticated employee claim.

## Decision

- Register the named authentication scheme `BranchDevice` only when Branch
  device authentication is enabled.
- Accept exactly one `Authorization` header using the HTTP authentication
  scheme `Device` and one opaque v1 credential token. Scheme matching is ASCII
  case-insensitive; token parsing remains exact and canonical.
- Return no result when the header is absent so later authentication mechanisms
  may participate.
- Return one generic authentication failure for malformed headers, malformed
  tokens, unknown credentials, wrong secrets, and inactive device or credential
  state. Do not disclose which check failed.
- Propagate cancellation and infrastructure failures instead of misreporting
  them as invalid credentials.
- On success, issue exactly one `pos_branch_id` and one `pos_device_id` claim
  from `VerifiedBranchDevice`. Never issue `pos_employee_id`, roles, permissions,
  or request-controlled identity values.
- Do not log or retain the Authorization header or token.
- Do not set a default authentication scheme or add authentication middleware,
  authorization policies, protected routes, or employee-session validation in
  this slice.

## Consequences

Later endpoint composition can explicitly select the `BranchDevice` scheme and
combine its identity with a separately validated employee session. Device-only
principals intentionally fail the existing complete request-identity resolver.

Automated tests use generated synthetic credentials and an in-memory validator
behind a temporary ready database. They contact no network or hardware.
