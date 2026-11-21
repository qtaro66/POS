# Frozen lifecycle report acceptance

This bundle covers migration, atomic capture, immutable reads, typed protocol,
report document rendering and recovery evidence. Use only synthetic records and
temporary/staging Branch data. No physical printer or cash drawer is required.

## Upgrade and capture

1. Back up a staging Branch database through its approved procedure. Upgrade from
   schema 33 to 34 using the normal Branch initialization/migration workflow.
   Existing report payloads, IDs, acknowledgement and retry fields must be equal
   before/after. Old detail responses show lifecycle:null.
2. Open a store with opening cash 500. Close its first shift without a count,
   open another shift with optional informational cash 200, close it with an
   actual count of 0, then open a third shift without a float and CloseStore with
   an actual count of 500. Verify store expected cash remains 500 with no sales;
   adding the informational 200 would be incorrect. The first shift is
   NotReconciled and the second actual-zero shift is Reconciled.
3. Inspect each report. It contains only the shifts known when that report was
   captured. The final store report includes all three original openings/closings
   and their original employee/device/time attribution, in operation order.
4. Reopen another store, create more shifts/sales and alter current master data.
   Re-read old detail: its base snapshot and companion are unchanged. Replay a
   previously accepted closing request and verify no second companion is written.
5. Fail companion insertion on a synthetic database using a test trigger. The
   close result, store state, cash snapshot and outbox row must all roll back.
   Remove the trigger and retry the same request successfully.

## POS report document

- Open a saved report from filtered history and inspect store opening context,
  per-shift informational floats, counted zero and uncounted rows. Display the
  uncounted shift list/count without turning it into cash shortage/overage.
- Open the readable document preview. It must contain only frozen report facts;
  confirming delivery afterward cannot change its cash/financial/table/opening
  facts or add a current timestamp. Live delivery diagnostics remain outside it.
- Open a legacy report with missing companion/cash/financial/table information.
  Missing facts are explicitly unavailable, not zero-filled or read from current
  timeline. A malformed existing companion is unavailable rather than replaced.
- Switch report/filter/employee/Branch scope while old reads are delayed. Previous
  responses cannot fill the new selection. Render hostile synthetic text as text,
  never executable HTML. Read failures do not change a committed close result.
- Preview is read-only. It must not call window.print, create a print job, access
  a printer, start a download or bypass Branch authorization.

## Delivery and scaling boundary

Capture a large synthetic multi-shift store. Local context can contain many shifts
without growing or rewriting the schema-1 Central payload. Read and confirm the
base report, reopen the Branch store and verify companion equality and unchanged
posted bytes. Both counted CloseStore and uncounted CloseShift wire recovery cases
must retain their local context through a lost reply and AlreadyAccepted retry.
Central receiver storage in automated HTTP tests remains simulated; no live
PostgreSQL or deployed mTLS handshake acceptance is claimed.

The companion stays at the Branch and is explicitly outside the existing receipt
scope. Full context transport/export and physical printing require later designs.
Central schema stays 2, Branch becomes 34. Obtain D21.3 approval before merge for
the migration/read-contract extension. Keep Back Office and Stock separate add-ons
and record full clean-commit verification in the Antigravity handoff.
