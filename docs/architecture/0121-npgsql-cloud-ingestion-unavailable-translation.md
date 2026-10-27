# Npgsql Cloud-ingestion unavailable translation

## Status

Accepted

## Decision

Translate transient Npgsql failures and direct timeouts from the PostgreSQL
Cloud-event ingestion store into
`BranchCloudEventPersistenceUnavailableException`. The store boundary covers
connection acquisition, insert, identity verification, commit, and asynchronous
session disposal.

Caller cancellation is rethrown unchanged. Non-transient Npgsql failures,
identity conflicts, invalid provider results, and programming defects are not
classified as temporary availability failures and continue to fail closed.

## Consequences

- Transient PostgreSQL outages reach the authenticated HTTP boundary as a
  bodyless `503 Service Unavailable`, so Branch delivery retains the event and
  retries under its existing policy.
- Permanent database or contract defects are not hidden behind a retryable
  status.
- The original provider failure remains the inner exception for controlled
  server-side diagnostics and is not serialized to the Branch.

## Boundaries

This slice does not add automatic retries inside Central, change Branch retry
timing, expose provider errors, or add a live PostgreSQL integration test.
