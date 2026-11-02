# PC POS data freshness status

## Scope and requirements

Implement the existing data freshness observations in D18.9's persistent sidebar
and the read-only portion of D18.10's System page. No business rule or versioned
Branch DTO changes. This is a partial System page, not completed health monitoring.

The authenticated ordering client reuses its bounded HTTPS transport for two
fixed same-origin Branch routes: `GET /api/v1/employee-data-sync-status` and
`GET /api/v1/cloud-data-sent-status`. Requests retain device and employee-session
headers, omitted cookies, no-store, redirect rejection, byte limits, timeout and
cancellation. Existing ordering routes and immutable command recovery remain
unchanged. Responses require schema version 1 and the correct nullable timestamp
field. Non-null timestamps must have an explicit UTC offset and valid calendar
date; malformed, disabled, unauthorized and failed responses are unavailable,
never an empty-success observation. No raw error body is presented or logged.

## Meaning of the displayed data

- **Data Synced** is explicitly limited to employee synchronization. Its current
  source cannot establish freshness of the whole Master Data package.
- **Data Sent** is the latest recorded acknowledgment from Central, not a live
  connection heartbeat, an empty-outbox assertion or proof that every event sent.
- UTC source timestamps appear with date and time in Asia/Bangkok. A null field
  means no record. Unsupported read-only host adapters, loading, and failed reads
  have distinct labels; none is presented as a healthy service.

The sidebar remains below navigation on every page. System explains provenance,
offers an explicit read refresh without a PIN, and identifies unavailable health,
database, checkpoint, outbox-count, printer and backup telemetry as unknown.
There is no overall green status, synthetic health-check time, inferred hardware
state, Retry/Reprint, configuration editing or device-test action.

## Lifecycle and isolation

An owner-scoped coordinator performs parallel independent reads when mounted and
on explicit refresh. It does not poll. Refresh immediately clears prior observed
timestamps while loading. An epoch and AbortSignal prevent late reads from
overwriting newer state; screen lock/unmount cancels pending reads. A replacement
authenticated client receives a new coordinator synchronously, so prior identity
data is not shown while effects run. Sidebar status navigation does not submit,
recover or alter a basket. Existing IndexedDB persistence restores it on return.

## Validation and remaining work

Safe tests cover exact authenticated GET routes and transport options, nullable
and seven-digit DateTimeOffset values, malformed/calendar-invalid/non-UTC data,
authorization/disabled endpoints, byte limits and cancellation. UI/coordinator
tests cover independent failures, unsupported adapters, explicit refresh, stale
responses, screen lock, identity replacement and basket preservation. Synthetic
development previews provide timestamp examples without hardware or production
access; production bundles exclude the development fixture.

Full health/readiness telemetry, printer and backup diagnostics, general Master
Data sync freshness, outbox counts, authentication bootstrap and real deployment
remain separate work. No backend, migration, dependency or financial calculation
is changed. Authenticated endpoint integration and identity-isolation behavior
retain explicit D21.3 approval before Antigravity merges the task.
