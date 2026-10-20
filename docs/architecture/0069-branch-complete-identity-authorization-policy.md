# ADR 0069: Branch complete identity authorization policy

## Status

Accepted.

Defines the authorization policy required by the inactive pairing-challenge
issuance endpoint without selecting or activating an employee authentication
scheme.

## Context

The issuance route requires an authenticated Branch, device, and employee
identity. Requiring only the presence of three claims would allow duplicate or
ambiguous identity values that the principal adapter must reject.

## Decision

- Register the named policy `BranchPosAuthenticated` only when Branch pairing
  is enabled.
- Require an authenticated principal and exactly one non-blank Branch ID,
  device ID, and employee ID across its authenticated identities.
- Use the same `AuthenticatedBranchRequestIdentityResolver` as the issuance
  adapter so authorization and request handling cannot disagree about identity
  completeness.
- Do not assign roles or a specific authentication scheme to the policy.
- Do not define employee PIN validation, employee-session creation or lifetime,
  issuance throttling, or route activation in this slice.

## Consequences

The reserved issuance endpoint now references a registered fail-closed policy
whenever pairing services are enabled, while remaining unreachable because the
route is still not mapped in `Program.cs`. A later employee-session design may
add an authentication scheme that produces the employee claim without changing
the endpoint contract or weakening exact identity validation.
