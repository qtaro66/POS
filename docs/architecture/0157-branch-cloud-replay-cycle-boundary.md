# Branch Cloud replay cycle boundary

The Branch runtime exposes a separate replay cycle interface. A cycle processes
at most one eligible replay request and reuses the existing `NoWork`/`Processed`
cycle result. `NoWork` also covers an earliest request blocked by active
ownership or retry scheduling; it does not assert that no incomplete requests
remain.

The implementation must claim through replay persistence, maintain ownership
during delivery, map the retained event to the existing authenticated Cloud
client, and complete only on matching durable event/sequence acknowledgment.
Known failures use replay-specific retry evidence and lease release. Ownership
loss or uncertain persistence fails closed; caller cancellation propagates.

This contract does not change normal outbox processing or its acknowledgment
and retry state. Cycle implementation, heartbeat integration, scheduling
priority between replay and normal work, and runtime activation remain
follow-up work. The interface itself performs no I/O and adds no wire fields.
