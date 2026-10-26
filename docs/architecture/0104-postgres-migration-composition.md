# 0104: PostgreSQL migration dependency composition

## Status

Accepted

## Decision

Compose the Central PostgreSQL migration runtime as singleton services from one
explicit connection string: the pooled connection factory, concrete Npgsql
locked-session factory, session-factory boundary, and migration executor.

Registration is idempotent and preserves the first registration. Building or
resolving this service graph creates the Npgsql data source but does not open a
database connection; network access begins only when the executor explicitly
opens a migration session.

Reject a null service collection or empty connection string before registering
services. Connection-string validation and secret-safe errors remain the
responsibility of the connection factory from the earlier data-source boundary.

## Boundaries

This slice does not read configuration, place a connection string in source,
invoke migrations at startup, connect to PostgreSQL, report readiness, register
the ingestion store, or activate Central persistence.

Automated tests build and resolve the dependency graph with a non-routable host
name and contact no database, network, or Branch hardware.
