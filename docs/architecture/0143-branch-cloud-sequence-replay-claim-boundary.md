# Branch Cloud sequence-replay claim boundary

Replay delivery uses a dedicated claim boundary rather than reopening the
normal Cloud outbox claim path. A replay lease carries both the immutable
recovery ID and the retained event identity and payload metadata needed to
reconstruct the original Cloud envelope.

The lease validates positive Branch and table versions, a non-negative replay
retry count, defined table-session action, UTC event and lease timestamps, and
an expiry later than its claim time. The claim store must select the incomplete
request with the lowest retained Branch sequence, then request order, and grant
at most one active lease for that request.

Claiming a replay must not clear or replace the retained event's original
`successfully_sent_at` acknowledgment. SQLite lease persistence, renewal,
failure scheduling, delivery, and completion remain separate follow-up slices.
This boundary has no database, network, or Branch hardware access.
