# ADR 0030: Disabled-by-default print spool retention runtime

## Status

Accepted.

Extends ADR 0026 and ADR 0029 without selecting a production retention policy.

## Context

ADR 0029 introduced the safe SQLite measurement and pruning boundary, but no hosted process invoked it. Requirement D19.4 provides a temporary 5 GB planning quota while explicitly requiring real spool measurements before the final policy is chosen. Runtime cleanup therefore must remain inert until deployment supplies a complete, reviewed policy.

## Decision

- Register a hosted retention service that is disabled by default and performs no database access while disabled.
- Enabling retention requires explicit positive values for terminal-payload retention, maximum total bytes, maximum deletes per transaction, and run interval. No production values are supplied as code defaults.
- Enabled retention requires the same existing, migrated, read-only validated Branch database used by print polling. Retention may run without enabling printer polling or composing printer transports.
- Startup fails closed unless exactly one retention processor is registered.
- Run one cleanup cycle immediately after startup, then wait for the configured interval. Log aggregate counts and bytes only; never log payload data or the database path.
- Derive the cutoff from `TimeProvider` and the configured retention duration. The processor remains responsible for the transaction-level protection rules from ADR 0029.
- Catch an isolated cycle failure, log it, and retry only after the configured interval. Host cancellation stops the loop.

## Consequences

Deployment can first enable a conservative measurement-backed policy without activating printer transport. The service cannot delete anything under repository defaults. The final retention duration, byte quota, batch size, and run interval remain deployment decisions that require production-like spool measurements and operational review.

Automated service tests use a fake retention processor. They do not open a production database, resolve a printer host, open a network socket, print paper, or open a cash drawer.
