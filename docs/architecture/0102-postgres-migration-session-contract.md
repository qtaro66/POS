# 0102: PostgreSQL migration-session SQL contract

## Status

Accepted

## Decision

Serialize Central schema migration sessions with one stable, positive PostgreSQL
transaction-scoped advisory-lock key. Acquire it through
`pg_advisory_xact_lock` using a parameter, so PostgreSQL releases the lock when
the surrounding transaction commits or rolls back.

Probe the schema-qualified migration table through parameterized `to_regclass`.
If present, read only `version` and `name`, ordered by ascending version, for the
fail-closed planner from ADR 0100. These control statements are read-only; only
the catalog migration selected by the executor may change schema or history.

The fixed lock key is part of the persistence contract and must not be changed
after deployment without a coordinated migration protocol, because mixed
versions using different keys could migrate concurrently.

## Boundaries

This slice defines SQL text, parameter names, table identity, and lock identity
only. It does not open a connection, begin a transaction, acquire a lock, read
history, execute migration SQL, or configure runtime database access.

Automated tests inspect the contract and contact no database, network, or Branch
hardware.
