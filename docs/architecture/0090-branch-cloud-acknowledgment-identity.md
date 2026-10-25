# 0090: Branch Cloud durable acknowledgment identity

## Status

Accepted

## Decision

Require every durable Cloud acknowledgment to include the event ID and
`branchSequence` that Cloud persisted. The Branch cycle processor compares both
values with the claimed outbox event using ordinal event-ID equality before it
marks the local event confirmed.

An acknowledgment with a different event ID or sequence is recorded as the
retryable failure `AcknowledgmentIdentityMismatch`. The local outbox event
remains unconfirmed and therefore eligible for a later retry in sequence order.

Retryable delivery results cannot contain an acknowledged event identity, and a
durable acknowledgment cannot contain a delivery error code.

## Boundaries

This decision defines application-level acknowledgment semantics only. It does
not define an HTTP status, response body, serialization format, endpoint, or
authentication scheme. A future concrete transport must map its response into
this validated result without weakening the identity comparison.

Automated tests use in-memory clients and contact no database, network, or
Branch hardware.
