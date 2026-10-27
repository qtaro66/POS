# Central Cloud-ingestion readiness health check

## Status

Accepted

## Decision

Adapt the PostgreSQL Cloud-ingestion readiness probe to ASP.NET Core's
`IHealthCheck` boundary. A ready probe maps to `Healthy`; every not-ready result
maps to `Unhealthy`. The health-check data contains only the expected and actual
schema versions, required-table flags, missing column names, and the stable
failure code.

Connection strings, credentials, server addresses, and raw exception messages
must never be added to the health result. Caller cancellation is forwarded to
the probe unchanged.

## Consequences

- Central hosting can publish standard readiness results without coupling the
  endpoint to Npgsql.
- Operators receive enough schema evidence to distinguish migration and schema
  failures without exposing secrets.
- The health-check name and readiness tag are stable constants for later
  registration and endpoint filtering.

## Boundaries

This slice defines and tests the adapter only. It does not register the health
check, map an HTTP endpoint, alter authentication, or contact PostgreSQL in
automated tests.
