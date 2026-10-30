# Central Cloud sequence-gap contract

Central now has a transport-independent exception contract for an event whose
per-Branch sequence is greater than the next sequence expected for that Branch.
The contract preserves the normalized Branch ID, the first missing sequence,
and the received sequence so later HTTP and Branch-delivery adapters can request
the missing event without treating the out-of-order event as durably stored.

The expected and received sequences must be positive, and the received value
must be strictly greater than the expected value. Older or equal sequences are
not gaps because they may represent an idempotent replay and remain subject to
the existing event-identity checks.

This slice defines the application boundary only. PostgreSQL continuity
locking, gap detection, HTTP response mapping, and Branch resend behavior remain
separate follow-up slices.
