# 0091: Branch Cloud service-origin configuration

## Status

Accepted

## Decision

Require `BranchCloudOutboxDelivery:ServiceOrigin` whenever Branch Cloud outbox
delivery is enabled. The value must be a normalized absolute HTTPS origin. DNS,
IPv4, and IPv6 hosts are accepted, with an optional explicit port.

Reject credentials, non-HTTPS schemes, paths, queries, fragments, surrounding
whitespace, missing hosts, and invalid ports. Validate the value during startup
before any delivery worker begins processing.

Disabled delivery requires no service origin and continues to perform no Cloud
network work.

## Boundaries

This configuration identifies only the trusted Cloud origin. It does not define
an endpoint path, HTTP method, authentication scheme, certificate policy,
serialization format, or acknowledgment response. No HTTP client is registered
or activated by this decision.

Automated tests validate configuration in memory and contact no network,
database service, or Branch hardware.
