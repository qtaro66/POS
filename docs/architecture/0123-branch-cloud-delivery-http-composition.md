# Branch Cloud delivery HTTP composition

The Branch host now has an explicit composition boundary for the Cloud delivery
HTTP adapter. It registers a named `HttpClient` pipeline and maps the existing
`IBranchCloudOutboxDeliveryClient` boundary to the HTTPS adapter without
overriding a deployment-supplied implementation.

The named pipeline is the extension point for the finalized Branch-to-Central
identity mechanism. This slice does not activate the adapter in the runtime and
does not add credentials. Runtime activation must occur only after the required
authentication handler is configured on the named pipeline.
