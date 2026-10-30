# SQLite Cloud replay readiness probe

The dedicated replay readiness probe opens an existing database read-only and
checks schema version and required request, lease, and failure table columns
within one read transaction. It reports missing tables and columns without
creating, migrating, repairing, or changing replay state.

Schema mismatch takes precedence over table and column errors. Unreadable or
missing databases fail closed. The probe complements normal outbox readiness;
it does not certify constraints, indexes, data integrity, network access, or
Cloud authentication. Runtime startup registration remains follow-up work.

Tests use temporary databases and contact no network or hardware.
