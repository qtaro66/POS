# POS saved closing report acceptance

Use synthetic records in an isolated test Branch. No physical devices or deployed
Central are needed for the automated cases below. This acceptance slice does not
replace the live PostgreSQL/mTLS staging checks documented in
pc-pos-central-closing-report-activation.md.

## Functional bundle

- Open the store-and-shift workspace and inspect the saved report list. Confirm
  initial loading, empty and unavailable states are distinct. Create a counted
  CloseStore and an uncounted CloseShift and inspect their original snapshots.
- Filter by Pending/Delivered, business date, store and shift. Load continuation
  pages with the same filter/limit. Append a new report after the first page; it
  must appear only after refresh, without duplicate older rows.
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

The automated Central wire tests use real Branch SQLite, the actual sender and
mapped receiver delegate with simulated Central persistence. They cover both
counted CloseStore and uncounted CloseShift and assert new page/detail transitions
through lost acknowledgement and restart. Dedicated query/API tests cover
pagination, isolation, redaction, financial snapshots and read-only state.
Frontend client/component tests cover protocol validation, filtered pagination,
selection/refresh races, legacy display and read failures.

## Deferred boundaries

No manual retry/delete/rewrite/configuration control or physical report printing
is introduced. Read access does not authorize a payment or printing mutation.
Back Office notification creation and Central staff report browsing remain
separate add-on work. Branch schema remains 33 and Central schema remains 2.
Record the final clean full-gate commit and handoff before Antigravity publishes.
