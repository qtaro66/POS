# Branch Cloud replay completion boundary

A replay completion couples the active replay lease with a durable Cloud
acknowledgment for that exact event ID and Branch sequence. Event comparison is
ordinal and case-sensitive. Acknowledgment time must be UTC and inside the
lease window, including claim time and excluding expiry.

The completion store must atomically close the matching recovery request and
release its lease after comparing persisted identity and ownership. A repeated
completion for the same recovery and event returns `AlreadyCompleted`, retaining
the first completion timestamp. Missing or cross-Branch recovery returns
`NotFound`; stale ownership fails closed. The original outbox acknowledgment
must remain intact.

SQLite persistence and runtime delivery integration remain follow-up work.
This application boundary has no database, network, or hardware access.
