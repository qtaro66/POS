# ADR 0077: Fail startup before using unavailable employee-session storage

## Status

Accepted.

## Context

The SQLite employee-session validator and issuance store require schema version
13 plus the device and employee-session tables. Runtime composition must not
discover a missing database, stale schema, or partial schema only after an
authentication request reaches storage.

## Decision

- Add `IBranchEmployeeSessionReadinessProbe` and a read-only SQLite
  implementation.
- Require the exact current schema version and both `branch_devices` and
  `branch_employee_sessions` tables.
- Return stable failure categories for schema-version mismatch, missing tables,
  and an unreadable database.
- Open SQLite in read-only mode and never create a missing database.
- Keep configuration binding, dependency-injection registration, employee
  status implementation, and runtime activation outside this slice.

## Consequences

A later runtime configuration validator can fail closed during startup with a
specific reason before employee-session authentication or issuance is exposed.
The probe does not mutate branch data and does not contact external services.
