# ADR 0076: Require an explicit employee session lifetime

## Status

Accepted.

## Context

Employee-session issuance requires an absolute future expiry, but the product
has not selected a default session duration. Hiding a duration inside issuance
or runtime composition would turn an unresolved policy decision into accidental
behavior.

## Decision

- Add `FixedBranchEmployeeSessionLifetimePolicy` as an implementation of the
  existing application boundary.
- Require callers to supply a strictly positive duration. The policy has no
  default value and does not read configuration directly.
- Require a UTC issuance timestamp and preserve UTC in the calculated expiry.
- Surface timestamp overflow as invalid policy configuration.
- Do not select a duration, bind configuration, register dependency injection,
  or activate employee-session runtime behavior in this slice.

## Consequences

Runtime composition can construct a deterministic policy only after an
explicit duration has been validated from deployment configuration. The
business decision remains visible, and tests can use the same production policy
without relying on wall-clock time.
