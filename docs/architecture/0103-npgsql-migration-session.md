# 0103: Npgsql PostgreSQL migration session

## Status

Accepted

## Decision

Implement the ADR 0101 session boundary with Npgsql. Opening a session opens one
connection, begins a `ReadCommitted` transaction, and acquires the transaction-
scoped advisory lock from ADR 0102 before returning control to the executor.

Inside the same transaction, probe for the migration table, read its ordered
history when present, and execute only SQL that exactly matches an embedded
catalog migration. Commit marks the session complete. Disposal before commit
rolls back, then disposes the transaction and connection; disposal after commit
only releases those resources.

Connection, transaction, or lock acquisition failures clean up partial resources
while preserving the original failure. Connection opening remains deferred until
the migration executor explicitly requests a session.

## Boundaries

This slice does not invoke the executor at application startup, configure a
connection string, run against a real PostgreSQL server in automated tests,
implement readiness reporting, or compose the ingestion store.

Automated tests prove construction is offline. End-to-end transaction, advisory-
lock, rollback, and DDL behavior still require an isolated PostgreSQL integration
test environment before production deployment.
