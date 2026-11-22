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

## Recovery acceptance matrix

| Scenario | Required outcome | Automated owner |
| --- | --- | --- |
| Base backlog grows every cycle | One base attempt followed by one eligible context attempt; no simultaneous sends | ClosingDeliverySchedulingTests |
| New base delivery fails | An older context with its own confirmed parent still progresses; the failed parent's context remains unclaimed | ClosingDeliverySchedulingTests |
| Context acknowledgement is lost | Parent receipt and frozen preview stay unchanged; exact context packet retries after reopen | ClosingDeliverySchedulingTests, ClosingLifecycleWireTests |
| Central parent and context acknowledgement are both lost | One bounded original-parent repair per context attempt; duplicate context delivery confirms the original stored content | ClosingLifecycleWireTests |
| Central outage | Pending remains visible, no early retry, reconnect after reopen completes delivery | ClosingLifecycleWireTests |
| Shutdown after Central context commit | The interrupted local lease remains in flight until expiry; replay receives AlreadyAccepted and confirms locally | ClosingLifecycleWireTests |
| Stored Central content conflicts | No overwrite or false confirmation; context conflict does not trigger parent repair; parent conflict stops repair | ClosingLifecycleWireTests |
| Authenticated branch is wrong | Central stores no context; correct identity later retries the exact packet successfully | ClosingLifecycleWireTests |
| Malformed Unicode in acknowledgement | Classify AcknowledgmentUnavailable for both base and context, retain pending work | ClosingReportDeliveryTests, ClosingLifecycleDeliveryTests |
| Missing or misleading response length | Read at most the context limit plus one byte; a stalled body is covered by the request deadline | ClosingLifecycleDeliveryTests |
| Lifecycle sending is paused and resumed | No claims while disabled; capped backoff and original packet survive reopen; parent receipt stays unchanged | ClosingLifecycleDeliveryTests |
| Context repair configuration | Lease must exceed three complete request deadlines; one authenticated client and one worker serve both queues | ClosingReportDeliveryTests |
| UI selection and refresh races | A late receipt cannot replace the selected report; stale success is cleared during refresh or failure | LifecycleRecovery.test.tsx |
| Context lease recovery is displayed | InFlight, LeaseExpired, RetryScheduled and Delivered stay outside the unchanged document preview | LifecycleRecovery.test.tsx |

The cycle is bounded by work count, not a fixed wall-clock duration: at most one base request, followed by a context attempt of up to three requests for missing-parent repair. Context lease time starts when the context is claimed, after the base attempt finishes. Shutdown cancellation stops the cycle rather than starting the next queue.

Focused commands from the repository root:

```powershell
dotnet test tests/Pos.Branch.Api.Tests --filter "FullyQualifiedName~ClosingReportDeliveryTests|FullyQualifiedName~ClosingLifecycleDeliveryTests|FullyQualifiedName~ClosingDeliverySchedulingTests"
dotnet test tests/Pos.Central.Api.Tests --filter FullyQualifiedName~ClosingLifecycleWireTests
npm exec --workspace apps/pos-web -- vitest run src/shell/LifecycleRecovery.test.tsx
pwsh tools/verify.ps1 -Quiet
```

## Staging recovery exercise

Use synthetic staging data and capture identifiers without secrets. Complete a close with lifecycle delivery paused, confirm that its parent receipt alone leaves the four-source overview Pending, then enable the companion sender. Interrupt connectivity before context confirmation, reopen the Branch service, and verify that the same context identity eventually becomes Delivered. Compare the saved document before and after; its content must be identical.

For a controlled Central restore drill, restore a disposable staging database to a point before the parent was accepted. The next context attempt should repair the original parent and confirm the companion without another closing command. If Central reports a content conflict, retain Pending and investigate; never edit immutable report rows to force Current. Record actual PostgreSQL migration, mTLS, restart and restore results separately from automated simulated-storage evidence.

No live PostgreSQL, deployed mutual TLS, production database or hardware was exercised. Before rollout, operators must validate migrations and authentication in staging, including temporary Central unavailability and reconnect. Never use real print or cash-drawer operations for these automated checks.

## Merge governance

Branch schema 35, Central migration 3 and the version-2 overview contract require explicit D21.3 approval before Antigravity merges. GitHub checks, including CodeQL, remain Antigravity responsibilities. Back Office and Stock remain separate add-ons.
