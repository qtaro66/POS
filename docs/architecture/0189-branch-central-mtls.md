# Branch-to-Central mTLS

Enable `CentralBranchMtls:Enabled` on a direct Kestrel HTTPS deployment. Kestrel requests client certificates and uses its normal certificate trust validation. Install the test Private CA trust on the relevant hosts. Keep the default validation in place and terminate TLS in Central Kestrel for this configuration.

Register each allowed leaf certificate under `CentralBranchMtls:Certificates` with `Sha256`, `BranchId`, and `Enabled`. The application derives the branch claim from that registration and requires the client authentication EKU. Disable or remove a registration to deny subsequent requests. For rotation, register the new fingerprint before replacing the Branch certificate, then retire the old registration.

Set `CentralCloudIngestion:AuthenticationScheme` to `BranchCertificate` when using this scheme for existing event ingestion. On Branch, set `BranchCentralMtls:Enabled` and configure absolute `CertificatePath` and `PrivateKeyPath` values in that section. Store the PEM files outside the repository and restrict access to the Branch service account. The HTTP handler keeps normal server certificate validation and disables redirects.

For the initial Central permission publisher, enable `CentralEmployeePermissions:Enabled` and configure a complete, versioned snapshot under `CentralEmployeePermissions:Branches`. Each branch entry has `BranchId`, a positive monotonically increasing `Revision`, and an `Employees` array containing `EmployeeId`, `Active`, and `SetSoldOut`. Keep deployment data outside source control. Update the complete snapshot atomically and increment the revision on every change. Inactive employees have no effective Sold Out permission.

The read route is scoped to the authenticated certificate branch. Missing or invalid configuration returns unavailable.

On Branch, enable `BranchEmployeePermissionSync:Enabled` and set `BranchId`, `CentralOrigin` (an HTTPS origin), and `CachePath` (an absolute file path in an existing private directory writable by the Branch service account). Use one cache per Branch process. The worker restores the last persisted snapshot and fetches at startup, then polls Central every hour. A signed-in employee on a provisioned PC POS can request an immediate refresh from Home. A refresh already in progress takes precedence. Only newer revisions for the configured branch replace the cache. The last valid snapshot is retained during outages.

Mobile Sold Out uses the cached permission alongside the active employee session and provisioned Mobile device role. A missing grant leaves the list view available. Validate a real HTTPS handshake, certificate rotation, and the Mobile workflow on target devices before deployment.
