# Branch provisioned device roles

For D08.2, the Branch deployment operator can register a device in the server configuration under `BranchDeviceAuthentication:ProvisionedRoles`. Each entry contains `BranchId`, `DeviceId`, and `Role` (`PcPos` or `Mobile`). Use the IDs recorded at device provisioning. Keep deployment-specific entries outside source control.

Branch authentication resolves the role from the branch and device IDs returned by credential validation. Missing, duplicate, and unsupported assignments omit the role claim and preserve existing authentication. Sold Out write authorization, Mobile employee permissions, and automated provisioning remain separate.
