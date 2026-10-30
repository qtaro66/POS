# SQLite Cloud sequence-replay claim store

The dedicated replay claimant uses an immediate SQLite transaction to select
the lowest incomplete expected Branch sequence, then request timestamp and
recovery ID. It checks retry eligibility and active ownership on that candidate
before writing its lease. An earlier blocked candidate prevents later replay
requests from being claimed.

Lease-state rows are created lazily. Reclaiming an expired lease preserves
retry count and scheduling state. The original outbox event, including its
successful acknowledgment, is never modified. Invalid event metadata causes
the transaction to roll back. The store opens only existing databases.

This slice implements persistence only. Replay renewal, failure scheduling,
completion, readiness, composition, and delivery integration remain follow-up
work. Tests use temporary SQLite databases and contact no hardware or network.
