# ADR 0043: Branch device credential validation composition

## Status

Accepted.

Composes the D11 token-authentication service and SQLite validator into the
Branch API runtime without exposing a protected HTTP route yet.

## Context

ADR 0042 provides the durable validator, but an ASP.NET host must not construct
it against a missing, stale, or partially migrated database. Authentication is
a fail-closed boundary: enabling it with unusable persistence must prevent the
Branch API from starting rather than silently disabling device checks.

## Decision

- Add an opt-in `BranchDeviceAuthentication` configuration section with
  `Enabled` and `DatabasePath` fields. It is disabled by default.
- When disabled, register neither `IBranchDeviceCredentialValidator` nor
  `BranchDeviceTokenAuthenticationService` and require no database.
- When enabled, require an absolute path to an existing Branch database.
- Add a dedicated read-only readiness probe that requires the exact current
  schema version and both `branch_devices` and `branch_device_credentials`.
- Validate configuration during host startup. Missing files, unreadable
  databases, schema mismatch, or missing tables fail startup with bounded
  messages that contain no credentials or database contents.
- Register one singleton `SqliteBranchDeviceCredentialValidator` behind its
  application interface and one singleton
  `BranchDeviceTokenAuthenticationService`.
- Use normal read-only SQLite access so committed WAL data remains visible.
- Do not register an authentication scheme, middleware, claims principal,
  endpoint, background service, or lifecycle writer in this slice.

## Consequences

Later HTTP authentication composition can depend on one validated runtime
service and cannot accidentally point at an uninitialized database. Default
development startup remains unchanged because the feature is off unless
explicitly configured.

Automated tests use temporary databases and generated synthetic credentials.
They contact no network or hardware and do not include deployment paths or
secrets in configuration committed to the repository.
