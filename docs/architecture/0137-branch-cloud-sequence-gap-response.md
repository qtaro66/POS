# Branch Cloud sequence-gap response parsing

The Branch HTTP delivery adapter now recognizes Central's versioned
`BranchSequenceGap` response on `409 Conflict`. It accepts the recovery hint only
when the schema version and error code are exact, the Branch ID and received
sequence match the event sent, and the expected sequence is positive and lower
than the received sequence.

A validated response remains a retryable failure and carries the expected
Branch sequence through the transport-independent delivery result. It is never a
durable acknowledgment. Malformed, oversized, non-JSON, stale, cross-Branch, or
otherwise inconsistent conflict payloads remain generic `HttpStatus409`
failures with no recovery sequence.

This slice does not change SQLite outbox state or select an event for resend.
Persisting and acting on the validated expected sequence remain separate
follow-up slices. Automated tests use in-memory HTTP handlers and contact no
network, database, or Branch hardware.
