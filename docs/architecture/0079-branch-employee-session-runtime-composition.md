# ADR 0079: Compose employee-session services without exposing transport

## Status

Accepted.

## Context

The employee-session lifetime, SQLite issuance, SQLite validation, readiness,
and configuration boundaries now exist. They need one disabled-by-default
composition root, but the authoritative employee-status source and HTTP
transport are separate concerns.

## Decision

- Bind and validate `BranchEmployeeSessions` at startup from `Program`.
- When disabled, register no employee-session runtime services and require no
  database, lifetime, or employee-status source.
- When enabled, compose the explicit lifetime policy, SQLite issuance store,
  SQLite validator, issuance service, and token-authentication service as
  singletons.
- Require the host to register `IBranchEmployeeStatusReader` before enabling
  composition; fail immediately instead of substituting a permissive or stale
  fallback implementation.
- Keep the feature disabled in default configuration with no default lifetime.
- Do not add an ASP.NET authentication handler, login endpoint, PIN validator
  implementation, authorization policy, or route activation in this slice.

## Consequences

The runtime graph can now be integration-tested end to end using synthetic
storage and an explicit employee-status reader. Production activation remains
fail-closed until the employee-status source is implemented and configured, and
no HTTP request path can use employee sessions yet.
