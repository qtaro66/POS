# Central Cloud sequence-gap HTTP semantics

Central maps a detected per-Branch sequence gap to `409 Conflict` with a
versioned JSON recovery payload. The payload contains the stable error code
`BranchSequenceGap`, the authenticated Branch ID, the first expected sequence,
and the greater sequence that Central received.

The response is not a durable acknowledgment. It gives the Branch enough
machine-readable information to select and resend the missing outbox event in a
later slice. Immutable identity conflicts remain bodyless `409` responses and
persistence unavailability remains `503`, so the transport does not conflate a
recoverable ordering gap with identity corruption or infrastructure failure.

The response contains no credential, secret, event payload, or customer data.
Automated tests exercise the mapper and HTTP handler without network or database
access. Branch parsing and missing-sequence resend behavior remain separate
follow-up slices.
