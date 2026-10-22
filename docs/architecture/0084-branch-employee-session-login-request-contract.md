# 0084: Branch employee session login request contract

## Status

Accepted

## Decision

Define schema version 1 of `BranchEmployeeSessionLoginRequest` with only the
presented employee PIN. The public JSON shape is `schemaVersion` and `pin`.
Branch, device, and employee identifiers are excluded: Branch and device
identity come from the authenticated device principal, while employee identity
comes only from successful PIN validation.

The PIN is secret input. Transport and application code must not log, persist,
cache, include in telemetry, or copy it beyond the minimum required for
validation. The request contract does not redefine PIN syntax because the
business PIN format remains undecided.

## Boundaries

This decision does not define supported-version or empty-PIN HTTP responses,
model-binding failure behavior, request-size or content-type limits, rate
limiting, HTTPS enforcement, endpoint metadata, route mapping, or runtime
activation.

Automated serialization tests use a synthetic PIN and verify that no trusted
identity fields are present. They contact no branch hardware, database, or
network.
