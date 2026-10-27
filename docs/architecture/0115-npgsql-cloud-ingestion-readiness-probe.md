# Npgsql PostgreSQL Cloud-ingestion readiness probe

## Status

Accepted

## Decision

Implement the Central PostgreSQL Cloud-ingestion readiness boundary with an
Npgsql adapter that performs metadata-only, read-only inspection. The probe
opens one connection, verifies the migration and event tables, reads the latest
recorded migration version, and compares the event-table columns with the
versioned schema contract.

The probe returns stable error codes without exposing connection strings,
credentials, or raw database exceptions. Caller cancellation is preserved.
Database transport failures are reported as unavailable, while malformed or
unexpected probe results fail closed as a probe failure.

## Consequences

- Readiness can distinguish connectivity, migration drift, missing tables, and
  missing columns without modifying PostgreSQL.
- The required-column list remains tied to migration version 1 and must be
  updated with future schema migrations.
- Construction and automated contract tests require no network access.

## Boundaries

This slice does not register the probe in dependency injection, expose a health
endpoint, provide production credentials, or run a live PostgreSQL integration
test.
