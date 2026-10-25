# 0092: Branch Cloud delivery-endpoint configuration

## Status

Accepted

## Decision

Require `BranchCloudOutboxDelivery:DeliveryPath` whenever Branch Cloud outbox
delivery is enabled. Resolve this path against the separately validated HTTPS
`ServiceOrigin` to produce the future delivery endpoint.

The configured value must be a canonical absolute URL path composed of one or
more non-empty segments. Segments may contain ASCII letters, digits, and the
unreserved characters `-`, `.`, `_`, and `~`. Reject absolute or network-path
URLs, trailing or repeated separators, dot-segment traversal, percent encoding,
queries, fragments, whitespace, and other characters.

Validate the complete origin and path during startup before any delivery worker
begins processing. Disabled delivery requires neither value.

## Boundaries

This decision makes the endpoint path deploy-time configuration; it does not
select a production path. It does not define an HTTP method, authentication
scheme, certificate deployment, request serialization, or response mapping. No
HTTP client is registered or activated.

Automated tests validate configuration in memory and contact no network,
database service, or Branch hardware.
