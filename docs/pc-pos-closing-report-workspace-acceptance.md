# POS saved closing report acceptance

Use synthetic records in an isolated test Branch. No physical devices or deployed
Central are needed for the automated cases below. This acceptance slice does not
replace the live PostgreSQL/mTLS staging checks documented in
pc-pos-central-closing-report-activation.md.

## Functional bundle

- Open closing reports directly from the main navigation using authenticated
  report reads without store-command capabilities. The store-and-shift browser
  and historical closing links must remain available independently.
- From a ready system-status overview, inspect pending original reports and
  pending opening/store/shift data separately. The companion action must use
  original status All plus lifecycle Pending, so an already confirmed parent
  remains visible. Disabled sending must not hide investigation of pending work.
- Loading, unavailable, unsupported or missing overview/read capabilities must
  not offer stale investigation actions. Queue changes before navigation may
  legitimately yield an empty fresh list. Repeated entry starts a new read and
  moves keyboard focus to the report heading.
- Open the store-and-shift workspace and inspect the saved report list. Confirm
  initial loading, empty and unavailable states are distinct. Create a counted
  CloseStore and an uncounted CloseShift and inspect their original snapshots.
- Filter by Pending/Delivered, business date, store and shift. Load continuation
  pages with the same filter/limit. Append a new report after the first page; it
  must appear only after refresh, without duplicate older rows.
- Combine the independent original-report and opening/store/shift-data filters.
  With a confirmed original report but pending companion, select original
  Delivered and lifecycle Pending: the saved report must remain discoverable.
  Confirming only the parent must not move it into lifecycle Delivered.
- Select lifecycle NotRecorded for a historical report without a captured
  companion or context job. Distinguish this from pending recorded work. If a
  recorded companion has a missing/corrupt job, show unavailable rather than an
  empty list or a historical NotRecorded result.
- Read the companion status directly on each list row. A parent marked
  Delivered may still have a queued, in-flight, expired or retrying companion;
  require its own receipt before showing companion Delivered. Attempts, last
  failure and confirmation time are companion metadata, not original amounts.
- Confirm an older server's omitted companion field appears unknown, while a
  modern explicit null means NotRecorded. A lifecycle-filtered response with
  absent or contradicting row metadata is unavailable, never silently accepted
  as matching. Legacy All queries may still show unknown rows.
- Open detail and compare list companion metadata with the nested report and
  top-level detail projection at the same read time. Refresh through retry and
  delivery transitions: only live metadata changes, never the frozen preview.
- Change lifecycle filters while detail or a continuation page is delayed.
  Abort or ignore the old request, clear its selection, retain all filters on
  continuation, and start fresh on reset. A version-1 cursor after upgrade is
  rejected and requires a first-page refresh.
- Compare saved receipt counts, cash applied, discounts, charges, rounding, VAT,
  attribution and table counts after later sales/master/session changes. Values
  remain those captured at close; legacy unavailable fields are not zero-filled.
- Select a closing from shift history and inspect its report/delivery record.
  Switch branch/employee scope, filters or selected report while old requests are
  delayed. Old responses must not populate the new view or reveal prior scope.
- Refresh during a delivery transition. Display settings, current delivery state,
  attempt count, previous failure category and latest confirmation separately
  from the frozen closing amounts. Enabled settings do not imply Central online.
- Inspect the system-status closing-report summary separately from existing
  table-event Data Sent. Read failures must preserve store-close availability and
  any pending lifecycle command already saved in the browser.

## Delivery and restart matrix

| Situation | Expected read result |
| --- | --- |
| Newly committed close | Queued, immutable snapshot available |
| Worker owns live lease | InFlight, receipt still unconfirmed |
| Worker stopped until lease expires | LeaseExpired, same report and original snapshot |
| Transport fails and retry delayed | RetryScheduled, sanitized last failure |
| Saved failure category is unknown | No raw text leakage; generic unavailable failure detail |
| Central commits but acknowledgement is lost | Pending/retry, never fabricated Delivered |
| Restart then AlreadyAccepted for same body | Delivered, same saved snapshot/report ID |
| Report read by another Branch | Empty list/404 detail, no existence disclosure |
| Corrupt cursor or mismatched filters | 400, no fallback to a broader query |
| Missing/corrupt report storage | Unavailable, no queue mutation |
| Original Delivered, companion Pending | Present only in matching independent filter combinations |
| Companion receipt confirmed after retry/reopen | Leaves lifecycle Pending and enters Delivered without snapshot changes |
| Historical companion not captured | NotRecorded; no inferred receipt or fabricated snapshot |
| Orphan/missing context job or malformed delivery metadata | Unavailable within the authenticated branch even if filters would hide it |
| Context state changes between continuation pages | Membership may change; no broad-query fallback and refresh begins a new traversal |
| Lifecycle filter says Delivered but returned companion is unconfirmed | Client rejects the page as unavailable |
| Parent is Delivered but companion metadata is omitted | Unknown for All; cannot verify a lifecycle-filtered response |
| Detail nested and top-level companion metadata contradict | Client rejects inconsistent detail |

The automated Central wire tests use real Branch SQLite, the actual sender and
mapped receiver delegate with simulated Central persistence. They cover both
counted CloseStore and uncounted CloseShift and assert new page/detail transitions
through lost acknowledgement and restart. Dedicated query/API tests cover
pagination, isolation, redaction, financial snapshots and read-only state.
Frontend client/component tests cover protocol validation, filtered pagination,
selection/refresh races, legacy display and read failures.

Per-row companion status evidence is covered by SqliteClosingReportStatusTests,
the mapped Lifecycle_report_status HTTP cases, and ClosingLifecycleWireTests.
Client contract cases reject false filtered membership, foreign identities,
invalid state/time combinations and contradictory detail projections. Component
cases distinguish all five companion states, historical null, old omitted
metadata, refresh and late responses. These checks use the regular test projects
and are included in the final tools/verify.ps1 gate; no optional live service is
needed for automated execution.

Navigation and application integration cases additionally verify system-status
drilldown, a dedicated read-only page, target mapping, fresh request revisions,
filter editing/reset, return to table work and cancellation on scope/session
replacement. Inspect local pending-command storage and mutation spies: browsing
reports must not change either. Preview and missing-capability states remain
non-operational. See architecture/0208-pos-report-investigation.md.

## Deferred boundaries

No manual retry/delete/rewrite/configuration control or physical report printing
is introduced. Read access does not authorize a payment or printing mutation.
Back Office notification creation and Central staff report browsing remain
separate add-on work. Lifecycle query filtering adds no migration beyond the
existing Branch schema 35 and Central schema 3. Per-row companion metadata adds
read response fields without changing cursor version 2 or immutable packets.
D21.3 approval applies to the read contract. No live PostgreSQL or deployed mTLS evidence is added
by the simulated Central receiver tests.
Record the final clean full-gate commit and handoff before Antigravity publishes.
