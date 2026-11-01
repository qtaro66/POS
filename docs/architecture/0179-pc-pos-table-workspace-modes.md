# PC POS table overview and selected-session workspace

## Scope

After verified MERGED PR #285, introduce the second part of the design review's
shared-shell step: distinct table overview and selected-session workspace modes.
D18.1's existing active-table split view remains the overview. Once authoritative
session detail is available, Open Table Workspace switches to accepted orders on
the left and the reserved menu area on the right, following D18.6's composition.

This is a read-only layout foundation. The menu area explicitly states that menu
and basket integration is unavailable. There are no add/submit/payment controls,
invented menu metadata, basket totals or local table-lock states. Existing
accepted totals retain their Branch source and read timestamp.
In the narrower session pane, the amount breakdown is a native disclosure while
the net total stays visible. Its footer has bounded scrolling so expansion cannot
consume the entire accepted-order viewport. No calculation changes are made.

## Ownership and navigation

Mode state records both the read-controller identity and selected tableSessionId.
Changing the authenticated client cannot reuse a previous mode, even if session
IDs happen to match. The read controller stays above mode presentation; entering
and returning do not fetch, write storage, reprice or alter commands. Returning
preserves the selected table. Main navigation also retains the mode while the
workspace owner remains mounted. Explicit refresh continues to clear stale
financial data before reads and displays failures without old totals.

Entering and returning focus the workspace heading. Screen lock unmounts the
entire owner as before. No basket store is read or mutated by this slice; durable
draft preservation and Basket/Sent binding remain the next composition work,
with the existing basket ownership/revision/recovery rules unchanged.

## Verification and limitations

Four regression cases cover keyboard opening/back, no extra reads and unchanged
amounts, main navigation/lock, client replacement with matching IDs, and failed
refresh. Existing read, privacy, basket and recovery suites remain applicable.
Synthetic desktop visual checks cover 1280x720 and 1920x1080.

The overview still contains only active sessions rather than a complete floor
plan. No real menu, categories, opening-time/status host data, Home/History/Store/
System adapters, production session provisioning, API/schema changes, or physical
printer integration is added. Default unauthenticated startup is unchanged.
