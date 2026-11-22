# Lifecycle context delivery

Branch schema 35 adds a durable companion queue to the frozen lifecycle snapshot introduced in schema 34. Central schema 3 adds immutable context storage with a foreign key to the original closing report. Original report packets, identities and delivery receipts remain unchanged.

Each captured companion is enqueued in the same SQLite transaction as its frozen snapshot. Migration backfills only existing snapshots; historical reports without snapshots remain unknown. A context is eligible only after its parent report has a local confirmed receipt. Claims, retries and acknowledgements use fenced, expiring leases. Confirmation changes delivery metadata, never the frozen preview.

The existing worker drains base reports before context work. Context delivery is opt-in through `ClosingReportDelivery:LifecycleEnabled`, default false; it requires base delivery and a lease longer than three request deadlines. Context requests use the existing mTLS client, HTTPS origin validation and bounded retry delays.

`POST /api/v1/closing-reports/lifecycle` accepts a version-1 packet with `contextId`, `reportId`, `branchId`, `requestId` and the original frozen `context`. The deterministic context identity is `lifecycle:` followed by the parent report identity. Requests are limited to 1 MiB. Central binds the authenticated branch to the packet, validates the context against its immutable parent and commits storage before returning Accepted or AlreadyAccepted. Changed content conflicts; a missing parent returns ClosingReportParentMissing.

Only that explicit missing-parent response triggers one replay of the original parent packet and one context retry per attempt, at most three HTTP requests. This repair does not re-close a shift, reset its local parent receipt, or overwrite conflicting Central content. Acknowledgements are limited to 8 KiB and must match version, branch and both identities. Lost or malformed acknowledgements remain pending and retries retain exact packet bytes.

Delivery overview is now schema version 2 with four delivery sources: tableEvents, closingReports, lifecycleReports and the existing replay counters. Any unconfirmed context prevents Current. LifecycleDeliveryEnabled describes worker configuration, not successful delivery. Report detail exposes separate lifecycleDelivery metadata outside the immutable preview. Older incompatible overview responses fail closed as unavailable; coordinate SPA and Branch deployment.

See [activation and acceptance](../pc-pos-lifecycle-context-delivery-acceptance.md). Back Office, Stock, printing, export and hardware integration are outside this change.

Base reports have priority. A continuously replenished eligible base backlog can delay companion sending; pending context remains visible until the backlog drains.
