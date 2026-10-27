# Central Cloud-ingestion readiness endpoint

## Status

Accepted

## Decision

Map an anonymous `GET /health/ready` endpoint only when Central Cloud ingestion
is enabled. The endpoint selects health checks tagged `ready`, disables response
caching, is excluded from OpenAPI, and uses ASP.NET Core's minimal plaintext
health response.

The response exposes only the aggregate `Healthy` or `Unhealthy` status. It does
not serialize health-check data, schema details, connection strings,
credentials, server addresses, or raw exception messages.

## Consequences

- Infrastructure can probe Central database readiness without credentials.
- Detailed schema evidence remains available inside the process for logs and
  diagnostics but is not published by this endpoint.
- Disabled Cloud ingestion maps no readiness route and retains no PostgreSQL
  dependency.

## Boundaries

This slice does not add a liveness policy, authorization requirement, detailed
diagnostic endpoint, rate limit, or live PostgreSQL integration test.
