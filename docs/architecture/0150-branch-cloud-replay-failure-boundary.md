# Branch Cloud replay failure boundary

A replay failure records its own immutable failure ID and exact replay lease,
UTC failure and next-retry times, error code, and optional lower missing Branch
sequence. Failure time must fall within active ownership; retry must be later.

The dedicated persistence boundary must atomically record evidence, increment
the replay retry count once, schedule retry, and release ownership. Repeated
failure identity with identical data returns the original retry result;
conflicting reuse and stale or completed ownership fail closed. It reuses the
existing delivery-failure result type for retry count and idempotency outcome.

Original event acknowledgment and normal outbox retry state remain intact.
SQLite failure persistence and runtime integration remain follow-up work.
This application boundary has no database, network, or hardware access.
