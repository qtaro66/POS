# SQLite Cloud sequence-gap failure evidence

Branch delivery failures now preserve an optional expected Branch sequence when
Central returns a validated sequence-gap response. The value must be positive
and lower than the sequence of the failed lease. Ordinary delivery failures
continue to store no expected sequence.

SQLite schema version 19 adds the nullable `expected_branch_sequence` column to
the immutable delivery-failure evidence table. Failure recording writes and
compares this value as part of idempotent replay validation, while the existing
outbox failure transition, retry count, next-attempt schedule, and lease release
remain unchanged and atomic.

The outbox cycle forwards only the validated value from the transport-independent
delivery result. Exceptions and acknowledgment-identity mismatches store no gap
guidance. This slice records evidence only; it does not reopen an acknowledged
event or alter claim order. Replay activation remains a separate follow-up.
