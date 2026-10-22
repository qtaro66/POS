# ADR 0080: Issue employee sessions only after PIN authentication

## Status

Accepted.

## Context

Employee PIN authentication and durable session issuance exist as separate
application services. A login operation must join them without allowing an
invalid or ambiguous PIN result to create a session, and without returning a
plaintext session token before persistence succeeds.

## Decision

- Add `BranchEmployeeSessionLoginService` as the application orchestration
  boundary for a trusted Branch and paired device.
- Authenticate the presented PIN through
  `BranchEmployeePinAuthenticationService` first. Return one null result for an
  invalid, inactive, or ambiguous employee and do not call session issuance.
- After successful PIN authentication, issue a session bound to the exact
  Branch, device, and verified employee through
  `BranchEmployeeSessionIssuanceService`.
- Return canonical identity, public session identifier, plaintext token, and
  expiry only after durable registration succeeds.
- Validate trusted identifiers before PIN authentication, forward cancellation,
  and propagate operational failures.
- Do not define PIN storage or syntax, attempt throttling, lockout, HTTP request
  or response contracts, response caching, cookies, headers, endpoints, or
  runtime activation in this slice.

## Consequences

Future transport code has one application entry point that cannot issue a
session before active-employee authentication and durable persistence. PIN
policy, abuse controls, and HTTP secret handling remain explicit later slices
instead of being hidden in this orchestration service.
