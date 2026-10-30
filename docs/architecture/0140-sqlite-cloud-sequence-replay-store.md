# SQLite Cloud sequence-replay store

Branch SQLite schema version 20 adds an immutable replay-request table keyed by
the application recovery ID. Each row references the retained expected outbox
event by Branch ID, outbox ID, and sequence, records the greater sequence that
revealed the gap, and preserves request and future completion timestamps.

The SQLite replay store uses an immediate transaction. An exact recovery retry
returns `AlreadyRequested`; reuse of the recovery ID for different data fails
closed. If the expected outbox event is no longer retained, the store returns
`ExpectedSequenceNotFound` without writing a request. A successful request does
not clear or replace `successfully_sent_at`, so the original durable
acknowledgment evidence remains intact.

Only one incomplete replay request may exist for a Branch sequence. A later
recovery ID targeting the same active sequence returns `AlreadyRequested`
without creating duplicate replay work; the delivery-failure evidence still
retains every detected gap independently.

This slice persists replay intent only. The regular outbox claimant does not yet
select replay requests, and confirmation does not yet complete them. Claim,
confirmation, and runtime activation remain separate follow-up slices.
Automated tests use temporary SQLite databases and contact no network or Branch
hardware.
