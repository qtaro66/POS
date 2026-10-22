# 0087: Branch employee session login request limit

## Status

Accepted

## Decision

Attach standard ASP.NET Core request-size metadata to the still-inactive
employee-session login endpoint. Limit the complete request body to 4,096 bytes.

The versioned request contains only a schema version and PIN, so the limit leaves
substantial room for JSON syntax and future compatible fields while bounding
server buffering and parsing work. The hosting server rejects oversized bodies
before JSON model binding, PIN validation, or session persistence.

The limit is a public constant beside the route metadata so hosting and tests
share one value. Rejections must not log or echo the request body or PIN.

## Boundaries

This transport ceiling does not define the business PIN syntax or maximum PIN
length. It does not change Kestrel's global body limit, proxy limits, rate
limiting, HTTPS enforcement, endpoint activation, or model-binding failure
responses.

Automated tests inspect endpoint metadata in memory and contact no branch
hardware, database, or network.
