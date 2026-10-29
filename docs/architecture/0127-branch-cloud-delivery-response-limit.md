# Branch Cloud delivery acknowledgment limit

The Branch Cloud HTTP client accepts at most 16 KiB for a successful
acknowledgment body. It rejects both declared and streaming/chunked oversized
responses as retryable failures before attempting to deserialize them.

Successful acknowledgments must use a JSON media type. Transport failures while
reading the bounded body remain retryable, while caller cancellation is still
preserved. The limit applies only to the small durable-acknowledgment contract
and does not change the immutable outbox request schema.
