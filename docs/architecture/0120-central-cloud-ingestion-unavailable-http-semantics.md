# Central Cloud-ingestion unavailable HTTP semantics

## Status

Accepted

## Decision

Define `BranchCloudEventPersistenceUnavailableException` as the application
boundary for a temporary Central persistence outage. The authenticated ingestion
HTTP handler maps this boundary to a bodyless `503 Service Unavailable` response.

The public exception message is stable and contains no connection string,
credential, host, SQL, or raw provider error. The original exception remains
available as the inner cause for controlled server-side diagnostics and must not
be serialized to Branch clients.

## Consequences

- Branch delivery can distinguish a retryable Central outage from an identity
  conflict without treating a durable acknowledgment as successful.
- HTTP responses do not expose PostgreSQL implementation details.
- Authentication and Branch-identity checks still occur before persistence.

## Boundaries

This slice defines the application and HTTP semantics only. The Npgsql adapter
does not yet translate provider connectivity failures to this boundary; that
translation remains a separate persistence slice.
