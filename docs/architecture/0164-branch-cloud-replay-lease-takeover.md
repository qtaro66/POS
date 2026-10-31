# Replay lease takeover during HTTP delivery

Safe integration tests hold the original worker's HTTP response, advance a
replacement worker's clock to lease expiry, and start another processor with fresh SQLite
stores. Once the replacement worker is delivering the same event, the original
worker receives either a matching acknowledgment or a retryable HTTP failure.

The original worker's clock remains behind so its result passes lease-window
validation and exercises persistence fencing independently of time validation.
Both stale writes are rejected by the real SQLite ownership predicates. The
replacement lease remains intact, recovery remains incomplete, and no failure
row or retry increment is created. The replacement worker can then acknowledge
and complete recovery, release its lease, and leave the original outbox durable
acknowledgment timestamp unchanged. A subsequent cycle reports no work.

Heartbeat delays are deliberately suspended to model worker suspension; this
does not validate real process scheduling, OS termination, or power failure.
HTTP responses are controlled in-process and no network or hardware is contacted.
Real Central authentication, PostgreSQL, and durable deduplication are outside
this evidence. No production code, schema, or business rule changes.
