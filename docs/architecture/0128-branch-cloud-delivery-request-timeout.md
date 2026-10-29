# Branch Cloud delivery request timeout

The Branch Cloud HTTP delivery pipeline uses an explicit configurable request
timeout instead of the platform default. `RequestTimeout` defaults to 30 seconds
and is applied to the named `HttpClient` used by the durable outbox adapter.

When delivery is enabled, startup validation rejects zero, negative, or values
above the maximum supported by `HttpClient`. A timeout remains a retryable
`RequestTimeout` result, while caller cancellation is still propagated and does
not become a delivery failure.

This setting bounds each HTTP attempt only. The existing lease heartbeat remains
responsible for retaining local outbox ownership while a request is in flight.
