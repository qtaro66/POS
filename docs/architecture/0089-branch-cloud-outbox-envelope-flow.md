# 0089: Branch Cloud outbox envelope flow

## Status

Accepted

## Decision

Require `IBranchCloudOutboxDeliveryClient` implementations to accept the
immutable `BranchCloudOutboxEventEnvelope` rather than the persistence lease.
The cycle processor projects the claimed lease immediately before delivery and
continues to retain the lease for heartbeat, confirmation, and retry handling.

This makes the application boundary enforce the decision in ADR 0088: future
delivery transports can access durable event data but cannot access lease
ownership, claim timing, retry count, or lease expiry.

## Boundaries

This change does not define or activate a concrete Cloud transport. It does not
choose an endpoint, authentication scheme, serialization format, schema version,
or acknowledgment response. Delivery results retain the existing rule that only
durable Cloud persistence may be acknowledged and every other completed result
remains retryable.

Automated tests use in-memory delivery clients and contact no database, network,
or Branch hardware.
