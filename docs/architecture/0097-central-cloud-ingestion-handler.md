# 0097: Central Cloud ingestion handler

## Status

Accepted

## Decision

Compose the versioned request mapper, Central ingestion application service, and
versioned durable response mapper behind one transport-neutral handler.

The handler validates and maps the request before persistence, forwards caller
cancellation, and returns a success response only after the ingestion service
has validated the durable persistence identity. Exact replay therefore returns
the same durable acknowledgment as first storage.

Uniqueness conflicts and all validation or persistence failures propagate out of
the handler and must not be converted into a success response.

## Boundaries

This decision does not activate an HTTP route or define authentication, HTTP
method, status codes, body-size limits, error responses, PostgreSQL persistence,
missing-sequence recovery, or retention.

Automated tests use in-memory stores and contact no database, network, or Branch
hardware.
