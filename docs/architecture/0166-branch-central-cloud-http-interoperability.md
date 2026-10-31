# Branch and Central HTTP interoperability

Safe integration tests connect the production Branch HTTP delivery client to the
production Central principal handler, ingestion service, and ASP.NET result
serialization through an in-process HTTP adapter. A test-only project reference
connects the two API assemblies; production projects remain independent.

The tests verify that Central's serialized sequence-gap response reaches Branch
as recovery guidance and that both stored and already-stored responses preserve
the event acknowledgment identity. Another case replaces the first successful
Central response with a simulated proxy 503 after storage, then verifies that
repeating the same event yields a matching acknowledgment and one stored event.

Central persistence is an in-memory test double, not PostgreSQL. Authentication
uses a synthetic principal and is not an authentication-provider or middleware
test. There is no real HTTP server, network, hardware, or durable storage in this
evidence. These tests validate existing contract interoperability; no production
behavior, schema, contract, or security configuration changes.
