# Recover pending store and shift requests

The store workspace can ask the Branch for the durable outcome of a pending
lifecycle request. An absent outcome keeps the pending request unresolved.
When the operator elects to retire an uncommitted request, the Branch records a
tombstone in the same immediate transaction used for lifecycle execution.

Retirement and execution serialize against one another. If execution already
committed, recovery returns that saved result. If retirement wins, a delayed
execution is rejected. Retirement leaves completed store/shift commands and
their closing snapshots intact.

Requests are Branch-scoped and tied to the original terminal and business
input. Recovery requires the authenticated PC POS identity. Retirement requires
an active employee PIN and records the confirming identity without persisting
the PIN. A request from another terminal or with changed business input cannot
be used to release a pending command.

The browser retains pending input until a durable completed or retired result
is known. A failed query or retirement keeps the request available for retry.
After resolution, the workspace reads current Branch state before offering
another lifecycle operation.

Schema migration and request-retirement behavior require D21.3 review before
merge. Regression evidence must include execution/retirement races, late
requests, restart, cross-Branch and cross-terminal attempts, unchanged completed
closings, PIN omission, reload recovery and the normal workspace entry point.
