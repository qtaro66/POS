# Branch Cloud replay cycle processor

The replay processor claims one eligible recovery, maintains its lease through
the replay heartbeat, and delivers the original immutable envelope using the
existing authenticated delivery-client boundary. It stops renewal before
persisting completion or retry evidence and uses the latest ownership token.

Only a matching durable event ID and Branch sequence complete recovery. Other
responses and ordinary transport exceptions record replay-specific failures
with bounded exponential backoff and optional server hints. Lower sequence
guidance requests an earlier retained recovery after failure evidence is saved.
A missing retained sequence fails closed.

Caller cancellation propagates without completion/failure writes. Renewal
failure cancels delivery and prevents persistence under uncertain ownership.
Normal outbox retry state and original acknowledgment evidence are unaffected.

No processor registration or hosted replay scheduling is added in this slice.
Cycle composition and scheduling remain follow-up work. Tests use controlled
clock, heartbeat delay, client, and persistence fakes without network/hardware.
