# Recovering successive missing Cloud sequences

Safe integration coverage starts with retained, previously acknowledged events
6 and 7 and a pending event 8. A controlled Cloud handler accepts sequences only
after 5. Delivery of 8 first requests 6; after replaying 6, another delivery of 8
requests 7. The serial scheduler replays 7 and eventually completes normal
delivery of 8. A second case injects a transient HTTP failure during replay of 7.

Both cases use real SQLite stores and the HTTP request/response mappings. They
assert the exact delivery order, two completed recovery requests, released replay
leases, persisted replay-failure count, and unchanged original acknowledgment
timestamps on 6 and 7. After completion, another cycle performs no HTTP delivery.

The clock and HTTP handler are controlled, with bounded polling and no network
or hardware access. This validates local orchestration under D12.7; it does not
validate real Central authentication, PostgreSQL durability, or restore behavior.
No production code, schema, or business rule changes.
