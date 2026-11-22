# Closing lifecycle context ingestion v1

POST /api/v1/closing-reports/lifecycle uses the existing BranchCertificate mTLS policy and HTTPS middleware. CentralClosingReports:Enabled must be true and schema 3 ready. Maximum request size is 1048576 bytes.

The JSON root contains schemaVersion (1), contextId, reportId, branchId, requestId and context. Context contains the original PascalCase frozen lifecycle snapshot from Branch. ContextId is lifecycle: followed by reportId; reportId uses the existing closing-report identity. Duplicate properties, inconsistent identities, invalid chronology and mismatched terminal closing facts are rejected. The authenticated branch must match branchId. Context content must match its already stored parent.

HTTP 200 returns schemaVersion (1), contextId, reportId, branchId and status (Accepted or AlreadyAccepted), only after the durable transaction commits. Identical retries are idempotent. Changed immutable content returns 409 ClosingReportContextConflict. Missing parent returns 409 ClosingReportParentMissing. Unavailable persistence/readiness returns 503; no receipt is issued. Authentication and validation errors never persist a context.

Senders must bound acknowledgement bodies to 8192 bytes and match all receipt identities. A lost receipt must retain the exact pending packet for retry. See architecture/0207-lifecycle-context-delivery.md for bounded parent repair and deployment constraints.
