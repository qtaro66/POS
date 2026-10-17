# ADR 0035: Disabled-by-default Branch command status query composition

## Status

Accepted.

Composes the read-only recovery boundary from ADR 0034 without exposing a
public endpoint.

## Context

The authoritative Branch command status query exists as an Application
contract with a SQLite adapter, but the Branch API cannot resolve it through
dependency injection. Enabling it against an absent, stale, or unrelated
database would make recovery behavior fail only when a Mobile request needs it.

The command query reads the live Branch database. Unlike the immutable print
startup inspection, it must allow a normal SQLite WAL file and use SQLite's
read-only transaction view of committed rows.

## Decision

- Add `BranchCommandStatusQuery:Enabled` as an explicit activation switch that
  defaults to `false`.
- Keep its database path explicit and independent from print-runtime settings.
- When disabled, register no `IBranchCommandStatusQuery` and require no
  database.
- When enabled, require an absolute existing database path, the current Branch
  schema version, and the `command_receipts` table before startup succeeds.
- Validate through a normal read-only SQLite connection so committed WAL data
  remains visible; do not create, migrate, checkpoint, or mutate the database.
- Register exactly one `IBranchCommandStatusQuery` backed by the read-only
  SQLite adapter.
- Do not add an HTTP route in this slice. Authentication, authorization,
  transport response mapping, and endpoint exposure remain explicit later
  boundaries.

## Consequences

The future authenticated Mobile recovery endpoint can resolve the authoritative
query through dependency injection after explicit deployment configuration.
Repository defaults remain inert, and enabling the query starts no hosted
service and contacts no network, printer, or cash drawer.

The command-query database path is temporarily separate from print-runtime
configuration. A future shared Branch database configuration can unify them
without changing the Application contract.
