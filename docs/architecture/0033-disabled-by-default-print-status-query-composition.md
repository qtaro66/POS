# ADR 0033: Disabled-by-default Print Job query composition

## Status

Accepted.

Composes the read-only boundaries from ADR 0031 and ADR 0032 without exposing a
public endpoint.

## Context

The authoritative single-job and attention-list queries exist as Application
contracts with SQLite adapters, but the Branch API cannot resolve them through
dependency injection. Registering them unconditionally would also make an empty
development configuration appear ready to serve status data.

## Decision

- Add `PrintStatusQueries:Enabled` as an explicit activation switch that
  defaults to `false`.
- When disabled, register neither query implementation and require no database.
- When enabled, apply the same absolute-path, existing-file, schema-version,
  required-table, and quiescent-WAL readiness validation used by the print
  polling and retention runtime.
- Register exactly one `IPrintJobStatusQuery` and one
  `IPrintJobAttentionQuery`, both backed by read-only SQLite adapters.
- Allow query composition without polling, retention, renderer, condition
  probing, transport, dispatcher, worker, or hosted service composition.
- Do not add an HTTP route in this slice. Authentication and endpoint exposure
  remain an explicit later boundary.

## Consequences

The future D18.10 system-status endpoint can resolve both authoritative query
contracts after explicit deployment configuration. Repository defaults remain
inert, and merely enabling read-only queries cannot open a printer connection or
start background work.

Automated composition tests resolve services only against temporary migrated
databases. They perform no network or hardware access.
