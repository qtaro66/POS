# ADR 0078: Validate employee-session runtime configuration before activation

## Status

Accepted.

## Context

Employee-session runtime composition needs an explicit database and session
lifetime. It must fail closed before activation when storage is unavailable or
the lifetime is omitted. The product still has no default duration, and an
authoritative employee-status implementation is not available yet.

## Decision

- Define disabled-by-default `BranchEmployeeSessionOptions` under the
  `BranchEmployeeSessions` configuration section.
- When enabled, require an absolute existing database path, a strictly positive
  lifetime, and a successful `IBranchEmployeeSessionReadinessProbe` result.
- Return specific validation failures for schema-version mismatch, missing
  employee-session tables, and unreadable storage.
- When disabled, require no database or lifetime and do not access storage.
- Do not bind these options in `Program`, register runtime services, provide a
  default lifetime, or activate authentication or issuance in this slice.

## Consequences

A subsequent composition slice can bind and validate these options on startup
without inventing policy values. Activation remains blocked until the current
employee-status source is implemented and explicitly registered.
