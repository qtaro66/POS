# PostgreSQL Cloud sequence-gap detection

Central PostgreSQL ingestion now serializes event acceptance per Branch with a
transaction-scoped advisory lock derived from the normalized Branch ID. After
acquiring the lock in its own statement, the transaction reads the next expected
sequence using the current committed snapshot.

An incoming sequence greater than that value raises the application sequence-gap
contract before any insert or durable acknowledgment. An equal sequence may be
inserted, while an earlier sequence continues through the existing immutable
identity comparison so exact retries remain idempotent and conflicting replays
fail closed.

The lock and sequence read use separate PostgreSQL statements deliberately. At
`READ COMMITTED`, this ensures a transaction that waited for another Branch
writer obtains a new snapshot after acquiring the lock and observes that
writer's commit. Hash collisions can serialize unrelated Branches but cannot
weaken correctness.

HTTP gap response mapping and Branch missing-sequence resend behavior remain
separate follow-up slices. Automated tests validate the SQL and orchestration
without contacting a live PostgreSQL server.
