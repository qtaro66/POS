# PC POS shared workspace navigation

## Scope

After verified MERGED PR #284, extract a reusable navy/teal shell around the
read-only table workspace. Follow D18.1's five navigation entries: Home, Tables,
Transaction History, Store and Shifts, and System Status. Start on Tables to
preserve the existing read-only host behavior. This is the shell portion of the
integration sequence in the PR #277-#282 design review, not full screen completion.

The four unconnected pages explicitly say they are unavailable. They expose no
commands, invented metrics, shift/operator/network indicators, or financial data.
Navigating to these pages makes no extra service calls. They are page destinations,
not permission gates; future adapters must enforce their existing command rules.

## State and privacy

The existing read controller remains owned by SentOrdersWorkspace, above the
shell's page selection. Navigating away unmounts the table presentation and any
open command-inspection panel, but keeps selected session and authoritative
read state. Returning does not silently refresh or reprice; the existing read-time
label and explicit refresh control continue to describe the data's provenance.
No basket store or journal is modified by navigation.

The main region is named after the active page and receives keyboard focus on
page changes. The active navigation button uses aria-current. Manual screen lock
still follows App's existing lock path and unmounts the workspace. A replacement
authenticated client gets a fresh read controller and cannot restore prior data.

## Validation and limits

Regression tests exercise all five destinations, unavailable states without
additional I/O, keyboard return preserving session/totals, client replacement,
and screen lock from an unavailable page. Existing acceptance/recovery tests
continue to apply. Automated tests use synthetic clients only.

This slice does not compose basket/menu entry, introduce Basket/Sent tabs, wire
real Home/History/Store/System adapters, provide production authentication,
change payment/table locking, or add APIs, schemas, dependencies, or hardware
access. The default unauthenticated production entry remains at sign-in. Existing
DEV-only sent-orders and command-recovery previews exercise the shell without
activating synthetic data in production.
