# Branch Cloud replay readiness startup

Cloud delivery startup now validates replay database structure alongside the
existing outbox options validator. Enabled delivery requires an absolute
existing database path and a successful replay readiness result. Failure reports
the error code and missing tables or columns through options validation.

Disabled delivery skips the replay probe. Its default SQLite implementation
is replaceable through dependency injection. Validation uses the same database
path as the composed stores and does not migrate or repair databases.

This activates startup validation only. Replay cycle and heartbeat integration
remain follow-up work. Tests contact no Cloud endpoint or Branch hardware.
