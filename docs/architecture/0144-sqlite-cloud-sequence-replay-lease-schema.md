# SQLite Cloud sequence-replay lease schema

Branch schema version 21 introduces `cloud_sync_sequence_replay_leases`, keyed
by recovery ID and linked to the retained replay request. Mutable replay lease
and retry state is separate from the request and the original Cloud outbox
acknowledgment. Existing replay requests remain intact during migration.

Each request can have at most one lease-state row. Retry count defaults to zero
and cannot be negative. Ownership, claim time, and expiry must either all be
absent or all be present with a nonempty owner and a valid expiry later than
the claim time. Deleting a replay request cascades to its lease-state row.

This migration provides persistence structure only. Claim, renewal, retry,
completion, readiness, and delivery integration remain follow-up work. Tests
use temporary databases and contact no hardware or network service.
