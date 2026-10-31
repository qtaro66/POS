# Hosted Branch Cloud replay recovery

A safe integration test starts the real Generic Host with
`AddBranchCloudOutboxDelivery`, real SQLite readiness validators and stores,
the default replay-first scheduler, polling service, and named HTTP pipeline.
A test-only handler factory substitutes the transport; it does not authenticate.

The database already contains an incomplete recovery request and a pending
normal event. Host shutdown cancels a blocked replay HTTP operation, leaves its
persistent lease and incomplete request intact, and creates no retry-failure
evidence. A fresh host with the same database and a controlled clock advanced
to lease expiry replays the retained event and then delivers the pending normal
event. The original acknowledgment timestamp remains unchanged.

The test uses a bounded deadline and awaits host shutdown before fixture cleanup.
It validates DI/startup/shutdown orchestration, not actual process termination,
service-manager behavior, authentication, PostgreSQL, TLS, or network delivery.
There is no hardware or network access and no production behavior change.
