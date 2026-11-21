# Closing report activation and recovery

This runbook covers the POS report transport and Central receiver. Back Office
notification creation and staff report browsing are separate add-on work.

## Preconditions

- Review and approve the D21.3 contract/security/migration changes before merge.
- Back up the Central database using the deployment's existing approved process.
- Provision trusted server certificates and registered Branch client certificates
  through the existing mTLS workflow. Do not place keys or connection strings in
  repository files, screenshots, handoff evidence or logs.
- Keep Branch ClosingReportDelivery disabled while upgrading Central.
- This release requires Central schema 2 and Branch SQLite schema 33. Do not run
  older Central binaries expecting schema 1 against the upgraded database.

## Central first

1. Supply ConnectionStrings:CentralDatabase through deployment secret settings.
   Enable CentralCloudIngestion and CentralBranchMtls; set
   CentralCloudIngestion:AuthenticationScheme to BranchCertificate.
2. Enable CentralClosingReports and start the Central API. The existing startup
   migration service applies migration 2 under its transaction/advisory lock.
3. Confirm /health/ready returns Healthy. Liveness /health alone does not establish
   report readiness. Verify a disabled/unregistered or foreign Branch certificate
   cannot submit another Branch's report. Never infer TLS authentication from a
   successful synthetic HTTP-handler test.
4. On an isolated staging database, send one synthetic saved Branch closing twice
   with the exact same bytes. Expect HTTP 200 Accepted followed by AlreadyAccepted
   with the matching reportId/branchId/schemaVersion 1, and one database row.
   Change the saved content under that ID and expect HTTP 409 without row mutation.

## Branch pilot

1. Configure ClosingReportDelivery:BranchId to the registered Branch and
   CentralOrigin to its trusted HTTPS origin. Use the existing BranchCentralMtls
   certificate/key configuration. Ordering and report delivery share one Branch
   database. Leave default timing limits unless deployment testing justifies a
   change; lease duration must exceed the complete HTTP deadline.
2. Enable ClosingReportDelivery for a staging Branch. A saved close should move
   from pending to confirmed only after the receiver commits and returns its
   matching acknowledgement. Use the POS store-and-shift queue refresh to inspect
   pending (including leased), leased and confirmed counts.
3. Exercise CloseStore with zero, positive and negative differences, and CloseShift
   without a count. Every close remains a report; an uncounted shift remains
   NotReconciled. No Back Office notification is created by this release.
4. Interrupt Central connectivity and close a staging shift/store. Confirm closure
   commits locally and the report stays pending. Restore connectivity and verify
   the original report reaches Central once logically, even if sent repeatedly.
5. Simulate receiver commit followed by a lost acknowledgement, restart the Branch
   worker, and verify same ID/body retry, AlreadyAccepted and eventual confirmed
   status. Test simultaneous senders and an expired lease without stale overwrite.

## Recovery and rollback

- HTTP 400/413/415 indicates an invalid/incompatible body or media type; preserve
  the original saved report and investigate the contract rather than editing
  historical data. HTTP 403 requires identity/certificate investigation. HTTP 409
  requires comparing report identities and immutable content; never overwrite a
  prior closing to make the retry succeed.
- HTTP 503/unavailable transport retains pending reports and uses bounded backoff.
  Repair readiness, migrations or connectivity, then allow the same report to
  retry. Avoid repeated process restarts as a retry mechanism.
- To pause delivery, disable Branch ClosingReportDelivery and restart the Branch
  service through the existing operating procedure. Leases expire and reports
  remain durable. Disable the CentralClosingReports route if required; table-event
  ingestion remains independently configured.
- Do not drop closing_reports, delete the outbox, reset delivery counters, alter
  snapshots, downgrade the migration history or install schema-1 binaries against
  schema 2 as a shortcut. A database restore or destructive rollback requires its
  own approved deployment plan. Re-enable the compatible receiver before senders.

## Evidence boundary

Automated coverage includes parser rules, migration planning/SQL contracts,
transaction store behavior, receiver/authentication and readiness boundaries, and
real Branch SQLite/HTTP transport-to-Central-handler recovery with simulated
Central storage. The host used for development has no PostgreSQL/docker runtime;
no live PostgreSQL migration, uniqueness-concurrency or TLS handshake pass is
claimed. Complete those staging checks before production activation. Physical
printers, cash drawers and payment terminals are not required for this slice.
