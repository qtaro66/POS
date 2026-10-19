# ADR 0045: Branch device authorization policy

## Status

Accepted.

Defines an explicit authorization boundary for endpoints that require only a
currently valid paired Branch device, without granting employee authority or
exposing a route.

## Context

ADR 0044 registers the named `BranchDevice` authentication scheme. Endpoint
composition still needs a named policy so device-bootstrap operations can opt
in without making that scheme the application default.

Device authentication is intentionally weaker than a complete POS request
identity. Protected ordering and payment routes also require an independently
validated Active employee and must not use this device-only policy.

## Decision

- Register `BranchDeviceAuthenticated` only when Branch device authentication
  is enabled.
- Bind the policy explicitly to the `BranchDevice` authentication scheme. Do
  not set a default authentication or authorization policy.
- Require an authenticated principal with `pos_branch_id` and `pos_device_id`
  claims. The named authentication handler remains the only component that
  issues those claims from durable credential validation.
- Do not require or issue `pos_employee_id`, roles, or permissions in this
  policy.
- Do not add authentication middleware, authorization middleware, an HTTP
  route, pairing behavior, or employee-session validation in this slice.

## Consequences

Future device-bootstrap endpoints can opt in to one narrowly scoped policy.
POS command endpoints remain blocked until a separate employee-session
boundary can produce the complete identity required by
`AuthenticatedBranchRequestIdentityResolver`.

When device authentication is disabled, neither the scheme nor the named
policy exists. Automated tests resolve service metadata only and contact no
network, database outside a temporary test file, or hardware.
