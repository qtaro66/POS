# Branch Cloud replay event envelope

`FromReplayLease` projects the retained event identity and original occurrence
timestamp into the existing Cloud event envelope. Recovery ID, retry count,
lease owner, claim time, and expiry stay local. Replaying does not mint a new
event or replace its original occurrence time with the replay time.

The resulting envelope has the same serialized durable fields as its original
normal-delivery envelope, supporting Central event deduplication. The explicit
method name preserves existing `FromLease` calls without overload ambiguity.

This slice provides mapping only. Replay cycle and heartbeat integration remain
follow-up work. Tests use immutable in-memory values and contact no network or
hardware. No public wire-format fields change.
