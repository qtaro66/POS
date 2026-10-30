# SQLite Cloud replay completion store

An immediate SQLite transaction checks the recovery's Branch and retained event
identity, conditionally records its first completion time against the exact
lease owner, claim time, and expiry, and releases ownership in the same commit.
Lease timing is validated by the application completion request.

Missing or cross-Branch requests return `NotFound`. A completed recovery for
the same event returns `AlreadyCompleted` without replacing its first timestamp.
Conflicting event identity or stale ownership fails closed. The original outbox
acknowledgment, replay request identity, and retry history remain unchanged.

This slice implements persistence only. Runtime composition and replay delivery
integration remain follow-up work. Tests use temporary databases and contact
no network or hardware.
