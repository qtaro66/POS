# 0086: Branch employee session login endpoint metadata

## Status

Accepted

## Decision

Define one inactive minimal-API endpoint for creating an employee session:
`POST /api/v1/employee-sessions`, named `CreateBranchEmployeeSession`.

The endpoint requires the existing `BranchDeviceAuthenticated` authorization
policy. It is not anonymous and does not require a pre-existing employee
session, because the endpoint creates that session after successful PIN
validation. Branch and device identity continue to come from the authenticated
device principal rather than route, query, or request-body values.

The endpoint delegates to `BranchEmployeeSessionLoginRequestHttpHandler` and is
excluded from generated OpenAPI while it is incomplete. The route mapper is not
called from `Program.cs` in this slice.

## Boundaries

Before activation, the endpoint still requires a reviewed PIN-attempt rate
limit, a small request-body limit, HTTPS enforcement, runtime composition, and
integration coverage. PIN syntax and maximum length remain undecided.

Automated tests inspect endpoint metadata in memory and use synthetic
collaborators. They contact no branch hardware, database, or network.
