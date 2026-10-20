# ADR 0067: Branch pairing issuance principal adapter

## Status

Accepted.

Adds a transport adapter that derives pairing-challenge creator identity only
from an authenticated principal with the complete Branch request identity.

## Context

Every authenticated POS employee may create a pairing QR without a repeated PIN
or role check. The issuance application handler already accepts a trusted
`AuthenticatedBranchRequestIdentity`, but ASP.NET endpoints receive a
`ClaimsPrincipal`. Binding Branch or employee identifiers from request data
would allow identity substitution.

Device authentication alone intentionally supplies no employee claim and must
not be sufficient to issue a QR challenge.

## Decision

- Resolve Branch, device, and employee identifiers exclusively through
  `AuthenticatedBranchRequestIdentityResolver` from authenticated claims.
- Return the same bodyless HTTP 401 response when the principal is absent,
  unauthenticated, incomplete, or ambiguous.
- Do not call the issuance service unless the complete identity resolves.
- Delegate valid requests to the existing issuance handler, preserving service
  origin validation, atomic token replacement, audit creator identity, request
  cancellation, and no-store response headers.
- Do not define an employee-session format, PIN policy, authorization role,
  authentication scheme, endpoint route, or runtime activation in this slice.

## Consequences

A future issuance route can bind the framework principal without accepting
Branch or employee identity from route, query, or body data. Device-only
authentication remains insufficient, while any future employee-session scheme
must produce the complete, exact claim set.

Automated tests use synthetic claims and an in-memory issuance store only. They
contact no network, database, or hardware.
