# 0075: Validate employee sessions from SQLite without owning employee status

## Status

Accepted

## Context

Branch employee-session authentication needs a read-only validator for the
session records introduced in schema version 13. A stored active session is not
enough: its device and employee must still be active, its expiry must be in the
future, and its presented secret must match the stored hash.

The branch employee cache and PIN schema are intentionally not defined yet.
Binding this validator to an assumed employee table would prematurely fix that
design.

## Decision

`SqliteBranchEmployeeSessionValidator` reads the existing session and device
tables in read-only mode. It requires exact branch and device binding, active
lifecycle states, a strictly future UTC expiry, and a fixed-time secret-hash
match.

Current employee status is obtained through the application-owned
`IBranchEmployeeStatusReader` boundary. The status reader is called only after
the token secret has been verified, so an unauthenticated caller cannot use the
validator to probe employee status.

Malformed tokens and inactive, revoked, expired, or mismatched records all
return the same unauthenticated result. Database corruption and operational
failures remain visible to the caller. Secret hash buffers are cleared after
use.

## Consequences

- No employee-cache schema is introduced by this change.
- No dependency-injection registration, endpoint, or runtime activation is
  added.
- A later composition slice must provide an authoritative implementation of
  `IBranchEmployeeStatusReader` before activating employee-session
  authentication.
- Schema version remains 13.
