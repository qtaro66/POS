# Branch Cloud outbox retry-delay composition

The Branch Cloud outbox runtime now composes the exponential retry-delay policy
and applies it whenever a delivery failure is persisted. The existing
`RetryDelay` remains the initial delay, while `MaximumRetryDelay` defaults to
five minutes and caps growth from the persisted retry count.

Startup validation requires both delays to be positive and rejects a maximum
shorter than the initial delay. The first failure keeps the existing initial
delay; later failures double deterministically until the cap is reached. The
calculated `next_retry_at` continues to be written atomically with retry count,
error code, processing status, and lease release.

The polling-service error delay remains unchanged because it represents a local
cycle failure outside a claimed outbox delivery attempt.
