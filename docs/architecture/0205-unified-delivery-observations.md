# Unified POS delivery observations

The POS Data Sent observation now covers table-event delivery, immutable closing
report delivery and sequence replay completion. It reads all three owning sources
inside one SQLite read transaction, independently of Data Synced (employee-cache
observations). This advances D18.9/D18.10 without adding Back Office functionality.

## Atomic scope and timestamps

The new SqliteBranchDeliveryOverviewQuery opens the existing ordering database in
read-only mode with one deferred transaction. Every source has a fixed,
parameterized authenticated Branch predicate. It performs SQL aggregation rather
than exposing event payloads, lease owners/tokens or raw errors. It never creates
a database, applies migrations, claims work, retries, acknowledges or deletes jobs.

A connection-local deterministic UTC key function normalizes valid source times
to seven fractional UTC digits before SQL MIN/MAX and schedule comparisons. This
retains 100ns ordering across existing Z and +00:00 formats, unlike millisecond
julianday comparisons. Invalid scoped timestamps or a missing source fail the
whole observation; there is no successful partial overview. Malformed data in a
different Branch cannot contaminate the current Branch's aggregate.

Per channel, total equals pending plus delivered. A non-null recorded
acknowledgement establishes delivered; processing labels alone cannot do so.
In-flight, delayed and expired-lease counts are mutually exclusive pending
subsets. Unknown pending metadata can overlap those subsets. Source-time fields
show the oldest pending creation and latest confirmed receipt, not heartbeat or
elapsed-network-health measurements.

Sequence replay is a separate recovery count, not an extra table transaction.
Any uncompleted replay blocks Current even if the original table event already
has an acknowledgement. Replay completion time participates in latest observed
acknowledgement. No replay or table-event sequence is created or rewritten here.

## State semantics

- Pending: any table event, closing report or replay remains unconfirmed.
- Current: all recorded work in these sources is confirmed and there is receipt
  history. This is a local-queue observation, not proof of full Cloud sync, remote
  availability or all future work being complete.
- NoHistory: no recorded pending work and no acknowledgement history. Empty
  sources never synthesize a successful-send timestamp.

lastConfirmedAt is the exact maximum of the three sources' receipt timestamps.
It is never a checkpoint claiming that all queues were empty at that time.
asOf is the server reference time for the observation. Work can change after the
snapshot; future-dated source timestamps from clock changes are not zero-filled
or dropped. The POS shows timestamp references rather than inferring connectivity.

## Runtime and browser boundaries

The authenticated PcPos GET /api/v1/ordering/delivery-overview returns a versioned,
branch-bound response and safe configuration booleans. A table sender is reported
configured only for the same Branch and normalized ordering database path. A
report sender uses ordering's database by composition and must match the Branch.
Flags reveal no paths, URLs, other Branch identities, certificates or secrets.
Configured does not mean the worker is healthy or Central is online.

The modern frontend reader strictly validates counts, state derivation and exact
UTC timestamp maxima, projects only public fields and never falls back to an old
table-event timestamp after modern HTTP/protocol failure. A truly legacy port
without the new reader retains only a clearly qualified table-event timestamp.

Sidebar Data Sent and System Status show covered-source pending counts alongside
last receipt and observation times. Modern System Status suppresses the old
independently read report aggregate, including during failures, so stale legacy
counts cannot contradict the atomic observation. Legacy ports retain their
explicitly scoped details. Home's existing Cloud summary uses the same
qualified observation. Global cards are not added to the store-and-shift main
area. Read failures hide stale success instead of showing zero pending.

While visible, the POS observes at startup and sequentially every 30 seconds
following completion. Visibility return and online events replace obsolete reads;
hiding, scope replacement or unmount cancels them. Each observation has a
10-second deadline, including readers that fail to settle after cancellation.
Accepted store/shift commands, recovered completions and existing accepted order
or cash recovery flows trigger fresh observations. A throwing status callback
cannot alter the accepted lifecycle result or prevent local recovery cleanup.

No schema changes: Branch stays at 33 and Central at 2. No network request to
Central, payment mutation, physical hardware action or privileged browser retry
is introduced. D21.3 approval applies to the authenticated read contract before
merge. Live PostgreSQL/TLS deployment acceptance remains a separate boundary.
