# Branch Cloud delivery HTTP client

The Branch Cloud outbox now has a concrete HTTP delivery adapter. It posts the
versioned immutable event envelope to the configured absolute HTTPS endpoint and
accepts a durable acknowledgment only when schema version, event ID, Branch ID,
and Branch sequence match the sent event exactly.

Every HTTP response other than `200 OK`, malformed response, identity mismatch,
timeout, or transport failure remains retryable. Caller cancellation is
preserved. The adapter does not define credentials: authentication must be
provided by the separately configured `HttpClient` pipeline after the Central
Branch identity mechanism is finalized.

Runtime dependency-injection activation remains a separate slice. This keeps
the HTTP contract and response classification independently testable without
enabling network access or inventing deployment credentials.
