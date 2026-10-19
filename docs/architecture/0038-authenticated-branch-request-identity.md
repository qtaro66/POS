# ADR 0038: Authenticated Branch request identity

## Status

Accepted.

Defines the fail-closed identity boundary required before exposing protected
Mobile routes.

## Context

D11 requires device authentication to remain separate from employee login. A
paired device alone cannot act as an employee, and every protected request must
be checked against the device's current Active or Revoked state. The D20.1
status handler also must receive its Branch identity from authentication rather
than Mobile-controlled request data.

Credential validation and employee-session validation are not implemented yet,
but their eventual authentication handler needs one strict output shape for
downstream routes.

## Decision

- Define explicit `pos_branch_id`, `pos_device_id`, and `pos_employee_id` claim
  types for the Branch API authentication boundary.
- Resolve an `AuthenticatedBranchRequestIdentity` only from authenticated
  identities in the request principal.
- Require exactly one non-empty value for every required claim. Missing,
  whitespace, or duplicate claims fail closed.
- Never combine claims from an anonymous identity with claims from an
  authenticated identity.
- Keep identity construction internal to the Branch API assembly so route
  handlers consume the resolver's validated result rather than independently
  reading claims.
- Update the command-status HTTP handler to require the validated identity
  object and use its Branch ID for the authoritative query.
- Do not add an authentication scheme or route in this slice. The future scheme
  must validate the presented device credential against current persisted
  device status and independently validate the employee session before issuing
  these claims.

## Consequences

The future route cannot accidentally accept `branchId` from a route, query
string, or body when calling the status handler. Pairing alone remains
insufficient because the identity also requires an authenticated employee.

This boundary does not claim that a principal is trustworthy merely because it
contains these strings. Only the future registered authentication scheme may
issue the authenticated principal after durable device and employee checks.
Automated tests use synthetic principals and contact no database, network, or
hardware.
