# Branch Cloud delivery retry-delay hint

The Cloud delivery result contract now supports an optional positive retry-delay
hint on retryable failures. Durable acknowledgments cannot contain this hint,
and existing failures without a hint retain their current behavior.

The hint is advisory transport guidance rather than durable acknowledgment. It
contains no event identity and cannot convert a failure into success. Invalid
zero or negative delays fail at the contract boundary.

This slice defines and tests the application contract only. Parsing an HTTP
`Retry-After` header and combining a hint with the local bounded retry policy
remain separate follow-up work.
