# PostgreSQL Cloud-ingestion readiness boundary

## Status

Accepted

## Decision

Define an asynchronous readiness-probe boundary for the Central PostgreSQL
Cloud-ingestion schema. A readiness result reports whether the database is
ready, the expected and observed migration versions, presence of the migration
and event tables, missing required event columns, and a stable error code.

The probe contract accepts cancellation and exposes the expected schema version
so host-level validation and health reporting can compare the database against
the same migration catalog used at startup. Readiness evidence contains schema
metadata only and must never include connection strings, credentials, or raw
database exception messages.

## Consequences

- Central health reporting can depend on a persistence-neutral result instead
  of Npgsql exceptions.
- Schema drift can be distinguished from database unavailability without
  mutating the database.
- Future schema versions can extend the evidence while retaining one probe
  boundary.

## Boundaries

This slice defines the contract only. It does not connect to PostgreSQL,
implement SQL inspection, register the probe, add a health endpoint, or change
startup behavior.
