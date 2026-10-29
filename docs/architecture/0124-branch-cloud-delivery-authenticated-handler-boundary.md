# Branch Cloud authenticated HTTP handler boundary

Branch-to-Central delivery now requires a deployment-supplied authenticated
HTTP handler factory. The named delivery pipeline resolves this boundary before
creating its client and fails closed when the factory or handler is absent.

The boundary deliberately does not select a credential format. A later
deployment slice may provide a client-certificate, token, or another approved
per-Branch identity mechanism without changing the outbox or HTTP contract.
Implementations must preserve normal server-certificate validation and keep
credentials, private keys, and production identifiers outside the repository.

Runtime activation remains deferred until a concrete approved handler factory
is configured.
