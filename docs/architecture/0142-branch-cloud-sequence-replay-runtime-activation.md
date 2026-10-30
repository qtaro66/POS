# Branch Cloud sequence-replay runtime activation

When Central rejects a delivered Branch event with an expected lower Branch
sequence, the Cloud outbox cycle first persists the delivery failure and
releases its lease. It then creates a replay request using the immutable failure
ID as the recovery ID, the expected and received sequences from that failed
delivery, and the same UTC failure timestamp.

Ordinary retryable failures do not create replay requests. Requested and
already-requested outcomes allow the cycle to finish normally. If the expected
event is no longer retained locally, the cycle fails closed after preserving
the delivery failure rather than silently continuing without recovery work.

This slice activates replay-request creation only. The regular outbox claimant
still does not select acknowledged events from replay requests, and Cloud
confirmation does not yet complete replay requests. Tests use in-memory fakes
and do not contact a network endpoint or Branch hardware.
