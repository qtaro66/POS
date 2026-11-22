# Lifecycle context delivery

Branch schema 35 adds a durable companion queue to the frozen lifecycle snapshot introduced in schema 34. Central schema 3 adds immutable context storage with a foreign key to the original closing report. Original report packets, identities and delivery receipts remain unchanged.

Each captured companion is enqueued in the same SQLite transaction as its frozen snapshot. Migration backfills only existing snapshots; historical reports without snapshots remain unknown. A context is eligible only after its parent report has a local confirmed receipt. Claims, retries and acknowledgements use fenced, expiring leases. Confirmation changes delivery metadata, never the frozen preview.

Each existing worker cycle attempts at most one eligible base report, then at most one eligible context. Each queue claims its own lease only when its turn starts; HTTP requests remain sequential. A sustained base backlog no longer prevents eligible context work. Context delivery is opt-in through `ClosingReportDelivery:LifecycleEnabled`, default false; it requires base delivery and a lease longer than three request deadlines. Context requests use the existing mTLS client, HTTPS origin validation and bounded retry delays.

`POST /api/v1/closing-reports/lifecycle` accepts a version-1 packet with `contextId`, `reportId`, `branchId`, `requestId` and the original frozen `context`. The deterministic context identity is `lifecycle:` followed by the parent report identity. Requests are limited to 1 MiB. Central binds the authenticated branch to the packet, validates the context against its immutable parent and commits storage before returning Accepted or AlreadyAccepted. Changed content conflicts; a missing parent returns ClosingReportParentMissing.

Only that explicit missing-parent response triggers one replay of the original parent packet and one context retry per attempt, at most three HTTP requests. This repair does not re-close a shift, reset its local parent receipt, or overwrite conflicting Central content. Acknowledgements are limited to 8 KiB and must match version, branch and both identities. Lost or malformed acknowledgements remain pending and retries retain exact packet bytes.

Delivery overview is now schema version 2 with four delivery sources: tableEvents, closingReports, lifecycleReports and the existing replay counters. Any unconfirmed context prevents Current. LifecycleDeliveryEnabled describes worker configuration, not successful delivery. Report detail exposes separate lifecycleDelivery metadata outside the immutable preview. Older incompatible overview responses fail closed as unavailable; coordinate SPA and Branch deployment.

See [activation and acceptance](../pc-pos-lifecycle-context-delivery-acceptance.md). Back Office, Stock, printing, export and hardware integration are outside this change.

Each context still requires its own confirmed parent, regardless of scheduler fairness. A failed base attempt does not prevent an older eligible context from getting its turn; shutdown cancellation propagates and leaves the interrupted lease for expiry recovery. A cycle may include one base request plus up to three context/repair requests, each under its own request deadline and queue-specific lease.

Acknowledgement JSON parsing rejects malformed UTF-16 escapes in property names or bound identifiers as AcknowledgmentUnavailable. Such responses retain durable pending work and use the configured retry schedule, rather than escaping as an unexpected worker exception.
