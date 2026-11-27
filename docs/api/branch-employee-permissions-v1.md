# Branch employee permission snapshot v1

GET /api/v1/branch-employee-permissions uses the existing Central Branch
certificate authentication and HTTPS policy. Central selects the configuration
for the authenticated Branch; a query parameter cannot select another Branch.
Responses are Cache-Control: no-store. This change adds no permission-management
endpoint or Back Office UI.

Snapshot fields remain schemaVersion=1, branchId, positive revision and employees.
Each entry contains employeeId, active, setSoldOut, optional openStore=false and
optional reprintKitchenOrder=false. The new optional field is backward compatible
with older snapshots and does not confer any permission when omitted.

Central configuration lives under CentralEmployeePermissions:Branches. An
employee's optional ReprintKitchenOrder must parse as a boolean when supplied;
malformed configuration returns 503. Central suppresses all permission flags for
inactive employees. Branch refresh uses the existing authenticated transport and
monotonically increasing revision. New permission or revocation values require
a newer revision; stale or foreign snapshots are not applied.

The Branch cache checks branch identity, Active and ReprintKitchenOrder for a
permission decision. Missing cache/employee/flag denies. Existing saved snapshots
restore with the same default-deny behavior. Duplicate JSON field names,
including differently cased aliases and duplicates within employee objects, are
rejected before Web deserialization. Invalid updates leave the last valid cache
unchanged under the existing synchronization policy.

ReprintKitchenOrder is only a permission input. The server-side authorization
building block still authenticates an active employee PIN on every invocation,
rechecks the latest cached flag afterward and binds requester/approver attribution
to one target. It creates no reusable grant and does not enable a print endpoint.
See architecture/0215-pc-pos-kitchen-reprint-authorization.md for the future command
boundary. D21.3 explicit approval is required before merge.
