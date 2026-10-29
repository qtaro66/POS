# Branch Cloud delivery HTTP runtime composition

When Branch Cloud outbox delivery is enabled, the runtime composition now
selects the concrete HTTPS delivery adapter and wires it into the existing
cycle processor. Deployment-supplied delivery-client implementations remain
authoritative and are not replaced.

The default adapter can be created only when an
`IBranchCloudDeliveryAuthenticatedHttpHandlerFactory` is registered. A missing
factory stops runtime composition instead of sending an unauthenticated
request. The concrete credential format and handler implementation remain a
separate deployment decision.
