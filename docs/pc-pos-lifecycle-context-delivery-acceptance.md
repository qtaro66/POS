# Lifecycle delivery activation and acceptance

## Deployment order

1. Back up staging data and deploy Central schema 3 through its migration runner. Verify readiness and existing mTLS branch authentication with CentralClosingReports enabled.
2. Deploy Branch schema 35 and the matching SPA together. Keep LifecycleEnabled false initially; existing closing reports continue independently. Stored contexts remain durably pending, including while this flag is disabled.
3. Enable ClosingReportDelivery:Enabled and LifecycleEnabled only after Central is ready. Retain valid BranchCentralMtls configuration and an HTTPS CentralOrigin. LeaseDuration must exceed three RequestTimeout deadlines (defaults: 60 seconds and 10 seconds).
4. Verify a staging close: base confirmation first, companion confirmation second, then four-source Current. Inspect independent base and context receipts outside the unchanged frozen preview.

Do not downgrade a migrated database. Disabling lifecycle delivery pauses sending but does not remove context work or claim it was delivered. Historical reports without captured context are not fabricated during upgrade. A context exceeding the receiver limit remains pending and requires investigation.

## Automated evidence

The local gate covers Branch migration/backfill and transactional capture, parent-dependent fenced leases, retries and corruption checks; Central migration planning, immutable persistence and commit failure, authorization and bounded parsing; actual HTTP packet adapters with simulated durable Central storage; and strict four-source UI contracts and separate context diagnostics.

Wire scenarios include normal delivery, lost context acknowledgement followed by database reopen, and missing Central parent repaired using the original packet. Existing parent payloads and receipts, stored snapshots and closing records are checked for stability. The focused checks are also included in tools/verify.ps1 through the normal test projects.

No live PostgreSQL, deployed mutual TLS, production database or hardware was exercised. Before rollout, operators must validate migrations and authentication in staging, including temporary Central unavailability and reconnect. Never use real print or cash-drawer operations for these automated checks.

## Merge governance

Branch schema 35, Central migration 3 and the version-2 overview contract require explicit D21.3 approval before Antigravity merges. GitHub checks, including CodeQL, remain Antigravity responsibilities. Back Office and Stock remain separate add-ons.
