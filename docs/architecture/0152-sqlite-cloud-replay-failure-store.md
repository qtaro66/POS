# SQLite Cloud replay failure store

An immediate SQLite transaction first checks immutable failure identity for an
exact retry. New failures conditionally increment replay retry count, schedule
the next attempt, release exact persisted ownership, and insert evidence in the
same transaction. Any evidence-insert failure rolls the lease update back.

Failure identity compares recovery, Branch, event, sequence, owner, claim and
expiry tokens, failure and retry times, error code, and lower sequence guidance.
Identical retries return the original retry count; conflicting ID reuse fails
closed. Completed, replaced, and missing ownership cannot record new failures.

Original outbox acknowledgment and normal outbox retry state remain unchanged.
Runtime composition and retry integration remain follow-up work. Tests use
temporary databases and contact no hardware or network.
