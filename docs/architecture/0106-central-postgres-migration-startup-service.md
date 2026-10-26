# 0106: Central PostgreSQL migration startup service

## Status

Accepted

## Decision

Represent fail-closed Central schema migration as a hosted startup service. Its
`StartAsync` method executes the locked migration runner from ADR 0101 and
returns only after the database is current. Schema drift, cancellation,
connection failure, or migration failure propagates and prevents successful
host startup.

Retain the successful execution result for local runtime diagnostics. Stopping
the hosted service performs no database operation because the migration session
is transaction-scoped and fully disposed before startup succeeds or fails.

## Boundaries

This slice defines and tests the hosted-service boundary but does not register
it with dependency injection. Therefore the running Central API still does not
connect to PostgreSQL or execute migrations. Runtime activation, readiness
reporting, integration testing, and ingestion-store composition remain separate
slices.

Automated tests use in-memory sessions and contact no database, network, or
Branch hardware.
