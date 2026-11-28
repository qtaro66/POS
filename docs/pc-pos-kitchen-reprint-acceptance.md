# PC POS kitchen reprint acceptance

This checklist covers the PC POS history workflow and its Branch queue. It is
not evidence of a successful physical print. Use isolated test identities and
captured test transactions. Never enter production credentials in a test report.

| Scenario | Required result |
| --- | --- |
| Open history and inspect original production | Read only; no new job or audit |
| Choose a submitted line with original print evidence | Show one frozen line, original table/station and copy explanation |
| Original print evidence unavailable | No enabled reprint action; explain missing evidence |
| Enter a rejected or unauthorized PIN | Show rejection, clear PIN, create no copy |
| Authorized PIN belonging to another active employee | Preserve login; Branch records requester and approver separately |
| Explicitly confirm one copy | Exactly one linked job, copy snapshot, command record and audit |
| Double-click confirmation or another tab races | At most one unresolved intent in that identity scope |
| Response lost after Branch accepts | Keep intent; status read recovers the same copy; no automatic POST |
| Reload with unresolved intent | Recover from workspace without selecting the old sale again; do not ask for a PIN just to read |
| Status is 404 or unavailable | Keep uncertainty and pending intent; never assume no job exists |
| Job is Unknown | Explain uncertainty; no automatic resend or new command |
| Storage fails or contains invalid data | Block sending; do not overwrite or silently discard pending intent |
| Lock or switch identity | Clear visible PIN and ignore late results; do not expose another identity's pending intent |
| Inspect saved recovery data | Only identity scope, command UUID and target; no PIN, reason or credentials |

For every queue case compare original orders, captured cash, cancellations,
production snapshots, original print jobs and outbox before/after. All must be
unchanged. A cancelled historical line uses its submitted quantity and options,
not its later chargeable quantity. Subsequent copy attempts require another
explicit operator action and fresh PIN authorization.

The automated browser fixture must contain no active physical printer adapter
or worker. Read-only SQLite inspection may verify resulting records; all writes
must go through the Branch API. Sanitized reports contain only case outcomes,
counts and provenance, never request bodies or fixture PINs.

Run the four browser cases with:

```powershell
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeKitchenReprint.ps1 -ReportPath .handoff/<exact-branch-directory>/edge-reprint.json
```

Queue and RejectedPin exercise the real Branch POST. LostResponse lets the
Branch accept the POST, drops its response and recovers after reload using GET.
Unknown intercepts only the GET observation to verify UI uncertainty handling;
the durable fixture job remains PrintQueued with zero attempts. This is not
evidence of an uncertain physical send. The earlier queue worker integration
test separately simulates failure after first-byte notification without hardware.

The existing Test-EdgeCashHistory.ps1 seven-case suite remains the read-only
history regression. Run both on the final clean commit for handoff provenance.

Physical acceptance remains pending until an operator explicitly confirms the
specific printer action at execution time. It should check the visible copy
label, exact original line/options/comment/table and original station routing.
Do not use a paper result to infer that a previously Unknown original job was
safe to resend automatically. Record hardware evidence locally, outside Git.
