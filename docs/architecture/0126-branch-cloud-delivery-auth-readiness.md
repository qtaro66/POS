# Branch Cloud delivery authentication readiness

The default Branch Cloud HTTP delivery path now validates its authentication
dependency during startup option validation. When delivery is enabled without a
registered authenticated handler factory, startup fails with a targeted
configuration error before the polling worker begins.

Disabled delivery requires no handler. A deployment-supplied delivery client
also remains authoritative and bypasses the default HTTP composition and its
factory requirement. The validation checks only that the approved deployment
boundary is registered; concrete credential loading and handler construction
remain separate deployment responsibilities.
