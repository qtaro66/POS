# 0082: Branch employee session login HTTP handler

## Status

Accepted

## Decision

The Branch API exposes transport semantics for employee session login through a
handler that receives a previously authenticated `VerifiedBranchDevice` and an
already parsed PIN. The handler delegates authentication and durable session
issuance to `BranchEmployeeSessionLoginService`.

A successful login returns HTTP 200 with the versioned
`BranchEmployeeSessionLoginResponse`. Invalid, inactive, or ambiguous PINs all
return the same HTTP 401 response so the endpoint does not disclose employee
state. Operational and persistence failures are propagated rather than recast
as authentication failures.

All handler outcomes apply `Cache-Control: no-store` and `Pragma: no-cache`
before authentication begins. The same shared response-header policy protects
pairing and employee-session secrets.

## Boundaries

This decision does not define the request-body contract, PIN serialization,
content-type or request-size enforcement, HTTPS enforcement, rate limiting,
endpoint metadata, route mapping, or runtime activation. Those remain separate
transport and composition decisions.

Automated tests use synthetic PINs and in-memory collaborators. They do not
contact branch hardware or production storage.
