# Branch Cloud replay lease-renewal boundary

Replay renewal uses a dedicated application interface. Implementations must
compare the persisted recovery and event identity, owner, original claim time,
and expiry token before extending an incomplete replay lease. Completed,
expired, replaced, and non-extending renewals fail closed.

The immutable lease's `Renew` method validates UTC renewal time inside the
active lease window and a positive duration producing a strictly later expiry.
It returns a new token preserving identity, original claim time, event metadata,
and replay retry count. The original token remains unchanged.

SQLite ownership comparison and runtime heartbeat integration remain follow-up
work. This application boundary has no database, network, or hardware access.
