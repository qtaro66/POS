# Replay recovery after interrupted local completion

A safe integration test injects an exception after the controlled Cloud handler
acknowledges retained sequence 6 but before local replay completion writes.
Persistent recovery stays incomplete, the original owner token remains leased,
and no delivery-failure evidence is fabricated for uncertain local persistence.

A newly constructed processor with new SQLite store instances cannot reclaim
the request before expiry. At expiry it resends the same event identity and
completes recovery on a repeated matching acknowledgment. The original outbox
acknowledgment timestamp remains intact and subsequent cycles report no work.

This models service reconstruction and a local persistence interruption, not
an actual OS process kill or power failure. Central idempotent acknowledgment
is simulated by the HTTP handler; real PostgreSQL, authentication, and network
validation remain outside this evidence. No production behavior changes.
