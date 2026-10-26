# 0105: Central PostgreSQL migration configuration

## Status

Accepted

## Decision

Compose the PostgreSQL migration runtime in the Central API only when Central
Cloud ingestion is enabled. Read the connection string from the standard named
configuration entry `ConnectionStrings:CentralDatabase`; never place a database
credential or production connection string in source-controlled settings.

When ingestion is disabled, register no PostgreSQL migration services and do not
require the connection string. When ingestion is enabled, a missing or blank
named connection string fails during service registration without echoing any
secret value. A valid value composes the offline service graph from ADR 0104,
but still does not open a connection or run migrations.

## Boundaries

This slice does not execute the migration runner, connect to PostgreSQL, validate
TLS policy, add production credentials, report database readiness, register the
ingestion store, or make enabled Central ingestion operational.

Automated tests use in-memory configuration with a non-routable host and contact
no database, network, or Branch hardware.
