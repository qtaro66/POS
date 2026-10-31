# Branch Cloud replay recovery integration evidence

Safe integration tests connect the serial scheduler, normal/replay processors,
SQLite claim/renewal/failure/completion stores, and the real HTTP delivery
client's JSON path. An in-process HTTP message handler simulates Central gap,
transient failure, and matching durable-acknowledgment responses.

The fixtures retain acknowledged sequence 6 while Central requests it after
rejecting sequence 7. Tests prove replay is delivered before retrying 7,
completion clears recovery ownership, and the original acknowledgment timestamp
remains unchanged. A second case injects a transient replay failure and verifies
persisted backoff, no delivery before eligibility, and eventual recovery.

This evidence does not exercise real Central PostgreSQL, authentication, sockets,
TLS, hosted-service lifecycle, or hardware. It supplements unit tests without
claiming production end-to-end certification. No production behavior changes.
