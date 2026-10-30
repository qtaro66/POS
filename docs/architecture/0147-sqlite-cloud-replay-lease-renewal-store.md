# SQLite Cloud replay lease-renewal store

Replay renewal validates the immutable token's active window and strictly
extending expiry before opening the database. A single conditional SQLite
update compares recovery ID, Branch and retained event identity, owner, claim
time, and current expiry, and requires an incomplete replay request.

Only the replay lease expiry changes. A stale token, replaced ownership,
completed request, or missing lease fails closed. The original Cloud outbox
acknowledgment and retry state are preserved. The returned token carries the
new expiry for subsequent ownership comparisons.

Runtime composition and heartbeat integration remain follow-up work. Tests
use temporary databases and contact no hardware or network.
