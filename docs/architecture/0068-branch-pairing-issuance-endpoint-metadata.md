# ADR 0068: Branch pairing issuance endpoint metadata

## Status

Accepted.

Defines the inactive HTTP route and security metadata for creating a pairing QR
challenge without registering its employee authorization policy or activating
the route.

## Context

Challenge issuance now derives Branch, device, and employee identity from the
authenticated principal and rejects device-only or incomplete identities. A
future route must not accept identity or PIN data in its request and must remain
unavailable until employee-session authentication can satisfy the complete POS
identity policy.

## Decision

- Reserve `POST /api/v1/pairing/challenges` with endpoint name
  `IssueBranchPairingChallenge`.
- Bind the route to the principal-based issuance adapter.
- Require the future named authorization policy `BranchPosAuthenticated`; do
  not allow anonymous access or assign a role requirement.
- Accept no request body by attaching a zero-byte request-size limit. Branch,
  employee, device, PIN, and service-origin values must not be supplied through
  route, query, or body input.
- Exclude the secret-bearing route from OpenAPI discovery.
- Do not register the named policy, map the route in `Program.cs`, define an
  employee-session scheme, or activate issuance in this slice.

## Consequences

The issuance endpoint contract is fixed and testable without making it
reachable. A later employee-session slice must register a policy that requires
an authenticated principal with exactly the complete Branch request identity
before the route can be activated.

Issuance attempt throttling and the employee-session lifetime remain unresolved
and must be decided before activation. Automated tests inspect endpoint metadata
in memory only and contact no network, database, or hardware.
