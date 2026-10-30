# SQLite Cloud replay failure schema

Branch schema version 22 adds dedicated replay failure evidence keyed by
failure ID. A composite foreign key binds recovery ID, Branch, retained event,
and sequence to the same replay request. Deleting a retained request cascades
to its failure records, matching the existing recovery retention structure.

Evidence stores original lease ownership and timing, failure and next-retry
times, error code, resulting replay retry count, and optional lower sequence
guidance. Constraints enforce positive counts, valid date windows, and lower
positive expected sequences. An index supports recovery-history lookup.

Migration preserves replay requests, lease state, and original acknowledgments.
Failure recording and idempotency logic remain separate follow-up work; this
slice does not activate retry scheduling. Tests use temporary databases and
contact no network or hardware.
