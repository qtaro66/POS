# Branch Cloud delivery Retry-After mapping

The Branch Cloud HTTP adapter now maps a valid `Retry-After` response header on
non-success responses into the delivery result's advisory retry-delay hint. It
supports both positive delta-seconds and future HTTP-date values.

HTTP-date calculations use an injected UTC time source for deterministic tests.
Malformed, zero, negative, or expired values are ignored, preserving the local
retry policy. A `Retry-After` header never changes a failed response into a
durable acknowledgment.

This slice maps transport guidance into the approved contract only. Combining
the hint with the local bounded retry policy remains a separate follow-up.
