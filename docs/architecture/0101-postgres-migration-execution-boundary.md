# 0101: PostgreSQL migration execution boundary

## Status

Accepted

## Decision

Execute Central PostgreSQL migrations only through a session that has already
opened a database transaction and acquired the migration lock. Inside that
session, read applied history, build the fail-closed plan from ADR 0100, apply
pending migrations in catalog order, and commit only after every migration
succeeds.

Schema drift, missing session data, cancellation, or SQL failure propagates and
must not commit. Disposing an uncommitted implementation session must roll back
the transaction and release the lock. Even a current database commits its
read-only migration transaction so the lock lifecycle is explicit.

The executor returns the initial version, final version, and applied count. It
does not expose connection strings, SQL errors as success, or partial progress
as a completed migration result.

## Boundaries

This slice defines and tests execution orchestration only. It does not implement
the Npgsql session, choose an advisory-lock key, connect to PostgreSQL, execute
SQL, configure startup migration, or activate database access at runtime.

Automated tests use an in-memory recording session and contact no database,
network, or Branch hardware.
