# 0085: Branch employee session login request handler

## Status

Accepted

## Decision

The employee-session login request handler accepts the versioned public request
and the authenticated device principal. It returns a bodyless HTTP 400 before
PIN validation when the schema version is unsupported or the PIN is null,
empty, or whitespace-only.

For a valid request shape, the handler passes the PIN characters without
trimming or normalization to the principal adapter. That adapter continues to
derive Branch and device identity only from authenticated claims and returns one
bodyless HTTP 401 for identity or credential rejection. Successful responses
remain the minimal versioned employee-session response.

Every outcome applies the shared no-store/no-cache response headers. The PIN is
never included in response bodies, errors, logs, or telemetry. Cancellation and
operational failures propagate through the existing login boundary.

## Boundaries

This decision does not define PIN syntax or maximum length, JSON model-binding
failure behavior, request-size or content-type limits, rate limiting, HTTPS
enforcement, endpoint metadata, route mapping, or runtime activation.

Automated tests use synthetic PINs, claims, and in-memory collaborators. They
contact no branch hardware, database, or network.
