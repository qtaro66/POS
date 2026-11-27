# Kitchen reprint authorization foundation

This prepares D01.4.1, D18.2 and D18.3 authorization before exposing a kitchen
reprint command. It adds a centrally assigned permission and a Branch-side
command-local PIN check. It does not enqueue, render, send or reprint anything.

## Permission synchronization

BranchEmployeePermissionEntry gains optional ReprintKitchenOrder=false alongside
the existing Active, SetSoldOut and OpenStore fields. SchemaVersion remains 1:
old snapshots omit the new field and deny it by default; old consumers can ignore
the additive field. Central configuration rejects an invalid boolean and emits
the permission only for an active employee in the authenticated Branch.

Branch accepts only the existing branch-bound, bounded, increasing-revision
snapshot protocol. CanReprintKitchenOrder requires a matching Branch and active
employee entry with the explicit permission. Restore preserves the last saved
valid revision. An omitted field, empty cache, wrong Branch or inactive employee
cannot grant permission. Malformed updates do not replace the last valid cache;
this follows existing offline synchronization behavior rather than inventing a
new expiry or forced Central lookup.

Before deserialization, reject duplicate JSON property names recursively and
case-insensitively. The existing Web serializer treats differently cased names
as aliases; ambiguous permission fields must not silently choose a granting
value. This hardening applies to the permission snapshot, not unrelated models.

## Command-local PIN check

KitchenReprintAuthorizationService is a server-side building block, with no HTTP
endpoint or UI action in this slice. It requires complete authenticated identity,
the provisioned PcPos device role and a cache for that Branch. Validate an exact
target consisting of command request, captured cash request, original production
snapshot, selected order line and original print job identities.

Each invocation uses the existing BranchEmployeePinAuthenticationService. It
copies the presented PIN into a working buffer and clears that buffer on both
success and failure. No PIN is retained in the decision, target, operator context
or logs. Missing/oversized PINs and malformed targets are rejected before calling
the validator. Known storage/validation failures fail closed; cancellation
propagates and cannot return an authorized result.

After PIN validation, read the latest synchronized permission again. A newer
revision applied while PIN validation was pending must take effect. No decision
is cached, persisted, serialized as a reusable grant or added to the login
principal. Every subsequent command must invoke the check again for its own
target and PIN, even when the previous command was authorized.

Following D18.3, the authorized active PIN approver can differ from the employee
who is logged in. Preserve both the original requester identity/device and the
approver identity in the command-local operator context, together with the exact
validated target. Do not switch the session employee or grant it extra claims.

## Boundaries for the future command

An Authorized decision is not evidence that a line or job exists or is eligible
to reprint. A future handler must validate the authenticated request and original
single-line snapshot/job, call authorization within that command, enforce its
transactional invariants and write the new linked job and audit. It must never
accept a client-supplied authorization decision or reuse a prior decision as a
capability token. The decision here writes no business or audit records.

Current schema 35 permits one original KitchenOrder job for a production
snapshot/station. Reprint persistence, original/new snapshot references, copy
labelling, station routing, idempotency/recovery, audit/result tracking and UI PIN
submission remain a separate implementation. D20 Unknown rules still prohibit
automatic resend after an uncertain physical outcome. No reprint button is
enabled by this foundation.

Tests cover Central/mTLS projection and revocation, cache compatibility/restore,
ambiguous JSON, branch/device isolation, per-call PIN validation, different
approver attribution, revocation during a pending PIN, cancellation and cleared
working PIN memory. No hardware, production credentials or deployed services are
required. No database migration or dependency is introduced. D21.3 explicit user
approval is required before merging the security/permission contract change.
