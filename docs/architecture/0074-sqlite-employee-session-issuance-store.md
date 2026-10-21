# ADR 0074: SQLite employee session issuance store

## Status

Accepted.

Adds durable employee-session registration to the Branch database without
selecting a session duration or concurrent-session policy.

## Context

ADR 0073 requires the plaintext session token to be returned only after its
verifier and lifecycle metadata are durably stored. The Branch API is the sole
writer of Branch SQLite, and session issuance must work without Cloud access.

The employee master-data schema and session validator are not yet implemented.
This slice must preserve employee identity for later current-status validation
without pretending that session existence alone proves the employee is Active.

## Decision

- Add schema version 13 with `branch_employee_sessions` containing the public
  session ID, Branch/device/employee binding, secret hash, UTC issuance and
  expiry timestamps, lifecycle status, and optional revocation metadata.
- Require a foreign key to the paired Branch device. Do not add an employee
  foreign key until the authoritative local employee cache schema is defined.
- Permit multiple Active sessions for the same principal. Concurrent-session
  limits and replacement behavior remain policy decisions and are not enforced
  by a unique partial index.
- Persist only `Active` or `Revoked` lifecycle state. Expiry is evaluated from
  `expires_at`; no background write is required merely because time passes.
- Make identity, secret hash, issuance time, and expiry immutable. A Revoked
  session cannot be reactivated.
- Add lookup and principal indexes, but do not prohibit a future retention or
  cleanup policy by making every session row permanently undeletable.
- Add `SqliteBranchEmployeeSessionIssuanceStore` using read-write mode without
  database creation. A single constrained insert is the atomic issuance-store
  operation.
- Copy the verifier hash from the registration and clear that operation-scoped
  copy after success or failure.
- Propagate uniqueness, foreign-key, cancellation, and storage failures. Do not
  retry session-ID collisions or return a plaintext token after failure.
- Do not add session validation, employee/PIN tables, lifetime values,
  revocation commands, dependency injection, login HTTP transport, or route
  activation in this slice.

## Consequences

Successful issuance can now be made durable locally before a token is returned,
while expiry duration and concurrent-session policy remain explicit future
choices. A later validator must join current device state and the authoritative
employee cache before accepting a session; an Active session row alone is not
sufficient authentication.

Automated tests use temporary databases only and contact no network or hardware.
