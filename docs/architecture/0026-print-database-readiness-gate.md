# ADR 0026: Read-only print database readiness gate

## Status

Accepted.

Extends ADR 0025 without changing its historical decision.

## Context

ADR 0025 required an existing database file but deliberately did not prove that the file contained the schema needed by the persisted print worker. An empty, corrupted, outdated, or partially provisioned file could therefore pass path validation and leave the enabled polling lane in a repeated error loop.

## Decision

- Before enabled print polling starts, open the configured database through an immutable SQLite file URI in read-only mode with pooling disabled. This prevents readiness validation from creating WAL/SHM sidecars.
- Fail closed before opening the immutable snapshot when a non-empty WAL file is present; recovery/checkpointing must complete through the normal database lifecycle first.
- Require the exact application schema version. Older versions need the normal migration workflow; newer versions require a compatible application release. Neither condition may be accepted silently.
- Require all tables directly read or written by the active print path to exist even when the schema version matches: `print_jobs`, `printer_profiles`, `print_attempts`, `rendered_print_payloads`, `bill_preview_snapshots`, and `production_order_snapshots`.
- Return stable diagnostic codes without including the database path, SQL text, or raw exception details.
- Never create the file, create tables, alter `PRAGMA user_version`, migrate data, or write any database content during readiness validation.
- Skip all file and SQLite access while print polling is disabled.

## Consequences

Enabled polling now fails startup before a worker loop can run against an incompatible database. Database creation, recovery, checkpointing, and migration remain explicit Branch Service provisioning responsibilities. The immutable check requires the Branch database to be quiescent and checkpointed during startup and deliberately does not consume uncheckpointed WAL content. This check proves structural readiness only; it does not prove that printer profiles or queued jobs contain operationally correct data.
