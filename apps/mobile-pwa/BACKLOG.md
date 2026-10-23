# Mobile PWA backlog

Authoritative delivery plan for `apps/mobile-pwa`. It changes only through
reviewed pull requests. Requirements are cited by ID from
`docs/requirements/POS-Requirements-v0.2/README.md`.

## Rules

- Only the user sets `Approved: yes`.
- Each task uses its own worktree and branch from the latest `origin/main`.
  Pull request and handoff state live on GitHub and in that worktree's
  ignored `.handoff/` directory (AGENTS.md); this file records PR links only.
- Three results are recorded separately for every task:
  - **UI**: `not verified` | `verified (fixtures)` | `verified`
  - **Integration**: `n/a` | `not verified` | `verified (<evidence>)`
  - **Device validation**: `n/a` | `not verified` | `verified (<devices>)`
- A result is recorded only after it is verified: the work is merged to
  `main`, its MERGED result is checked, and the evidence (merged PR,
  validation report, integration run or named devices) is cited. Results are
  never set in advance of that, including in the task's own pull request.
- The UI result carries the implementation and test evidence (merged PR and
  its `verify.ps1` report). It never implies integration with a running
  Branch Service or validation on a real device; those are recorded only
  under their own results, with their own evidence.
- A task is complete only when every result is `verified` or `n/a`.
  Integration needs are listed per task as Backend capabilities. Device
  validation that cannot run stays `not verified`.
- Backend needs are stated as capabilities (CAP-xx). The endpoint and contract
  are agreed with the Backend owner; Mobile never invents or changes them.
- Persistence and real-device criteria are listed only for tasks they apply to.

## Definition of done (every task)

- UI: every state the task introduces (loading, empty, error, offline, stale)
  is handled; text comes from `src/strings.ts`; business values come from the
  Branch Service or fixtures, never calculated on the client.
- Accessibility: WCAG 2.2 AA for the task's screens (labels, focus order and
  visible focus, 44 px touch targets, contrast, status announcements), with
  automated checks in tests.
- Offline: a local save may be confirmed truthfully as saved on this device.
  Branch acceptance, or success of a business command such as `OpenTable`,
  `SubmitOrder`, `RequestPayment` or a cancellation request, is shown only
  after a Branch result.
- Tests cover every acceptance criterion; `pwsh tools/verify.ps1 -Quiet`
  passes on the clean final commit; the README is updated.
- Fixture-only behavior is recorded under Integration and in the README.

## Pending decisions

| ID | Decision | Blocks |
| --- | --- | --- |
| DEC-1 | UI language: Thai, English, or both (no Mobile wording is confirmed) | Final wording; not structure |
| DEC-2 | Recovery of local Drafts/outbox after re-pairing (new `deviceId`) | MOB-11 |
| DEC-3 | QR decoding library for iOS Safari (new dependency) | MOB-13 |
| DEC-4 | Minimum Android and iOS versions (NFR-10) | MOB-16 |
| DEC-5 | Production Mobile stack: installable PWA, cross-platform or native (D17, CORE-08) | Final Mobile completion |
| DEC-6 | Handling of Drafts after `Expired/NeedsReview` (deletion workflow) | MOB-11 deletion step only |
| DEC-7 | Requirement conflict: D10.5 blocks a Draft for a `PaymentPending` session and forbids retrying it into that session, while D12.3 keeps the Draft so it can be sent again if the PC reopens ordering (`CancelPaymentRequest`). Can a blocked Draft be resubmitted to the same session after it returns to `Ordering`? | MOB-11 resubmit path only |
| DEC-8 | Whether Mobile basket lines carry `fulfillmentType` (`DineIn`/`Takeaway`). D18.6 confirms it per Order Line for the PC POS menu page only; D10 does not mention it for Mobile | Mobile Draft line model and CAP-08 payload |

Confirmed and not pending: unsent Drafts become `Expired/NeedsReview` after
30 days (D19.4, NFR-06). D19.4 does not say where the 30 days start; Mobile
counts them from when the Draft was started, the literal reading of "kept
30 days". Pending and Submitted requests never expire.

## Required Backend capabilities

| ID | Capability | Needed by |
| --- | --- | --- |
| CAP-01 | Mobile can reach the Branch API from the PWA origin, and the endpoints are in the OpenAPI description (D17) | all integration, MOB-14 |
| CAP-02 | End an employee session on the Branch | MOB-03 |
| CAP-03 | Report Branch reachability and employee-data sync time to an authenticated device (3.3) | MOB-03 |
| CAP-04 | Tell a revoked device apart from a rejected PIN (3.3, D11) | MOB-03, MOB-11 |
| CAP-05 | List the branch's tables with status and `tableVersion` (D10.4) | MOB-04 |
| CAP-06 | Open a table idempotently, returning the existing session if open (D03, D10.4) | MOB-04 |
| CAP-07 | Menu for a table session: items, option groups, resolved prices, sellability, `masterDataVersion` (D08.3, D08.4, D09) | MOB-05 |
| CAP-08 | Accept a whole Draft as one order batch, idempotent by `requestId`, with machine-readable rejection reasons per line (D12.3, D20.1) | MOB-08 |
| CAP-09 | Query a command result by `requestId` (D20.1; handler exists, not routed) | MOB-08, MOB-09, MOB-10 |
| CAP-10 | Table session detail: lines, batches, Branch totals, cancellation state, `tableVersion` (D10.1) | MOB-07 |
| CAP-11 | Request payment, idempotent (D10.2) | MOB-09 |
| CAP-12 | Create and read cancellation requests (D10.3, D06) | MOB-10 |
| CAP-13 | Set or clear Sold Out, and expose the `SetSoldOut` permission (D08.2) | MOB-12 |
| CAP-14 | Record the original Draft creator and the employee who took it over (D10.5) | MOB-11 |
| CAP-15 | Employee display name and branch name for the paired device | MOB-03 |

## Tasks

### MOB-01 Employee-session expiry
- Approved: yes
- UI: verified (fixtures) | Integration: not verified | Device validation: not verified
- UI evidence: PR #139 squash-merged as 27bba11; `verify.ps1` PASS on
  db1d4e0 (135 Mobile tests); merged `apps/mobile-pwa` content identical to
  db1d4e0. The Branch adapter is covered by unit tests with a fake fetch only.
- Integration needs: CAP-01 (end-to-end login against a running Branch Service)
- Depends on: none
- PR: https://github.com/qtaro66/POS/pull/139
- Acceptance: session ends at the Branch `expiresAt`; token discarded; PIN
  login with expiry message; recheck on resume and input; pairing and local
  data kept; fixture sessions without `expiresAt` never expire.
- Device: background/resume expiry on one Android and one iOS device.

### MOB-02 App shell, navigation and accessibility baseline
- Approved: yes
- UI: verified (fixtures) | Integration: n/a | Device validation: n/a
- UI evidence: PR #141 squash-merged as 0740091; `verify.ps1` PASS on
  e1a08a1 (196 Mobile tests, including the static contrast and target-size
  test); merged content identical to e1a08a1. Layout at 320x640 and 640x320,
  100% and 200% text, checked in a desktop Chromium pane, not on a phone.
  Shared components are `Screen`, `Alert` and `Notice`; buttons and lists
  share CSS classes, and the existing sync bar is the live status region.
  No manual screen-reader pass was made.
- Depends on: none (no pending decision)
- PR: https://github.com/qtaro66/POS/pull/141
- Scope: authenticated shell with phone-first navigation between Tables,
  the current table, and Outbox/status; route state without new
  dependencies; shared accessible components (screen heading focus, live
  status region, alert, button, list); safe-area insets.
- Acceptance:
  - Navigation works by touch and keyboard; each screen change moves focus
    to its heading and is announced.
  - Touch targets at least 44 px; visible focus; text contrast 4.5:1.
  - Layout works from 320 px wide, in portrait and landscape, with 200% text.
  - All new text in `src/strings.ts`; no language is added (DEC-1).
  - Existing pairing, login, expiry and table-list tests still pass.

### MOB-03 Real connection status, logout and identity display
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-02, CAP-03, CAP-04, CAP-15
- Depends on: MOB-02
- Acceptance: offline banner and employee-data sync time from the Branch;
  logout reaches the Branch but local logout never waits for it; a revoked
  device returns to pairing with an explanation; names shown when provided.

### MOB-04 Floor plan and OpenTable
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-05, CAP-06
- Depends on: MOB-02
- Acceptance: all tables with Branch-reported status; opening an open table
  returns its existing session; p95 at most 500 ms to show or select a table
  with fixtures (D19.1).

### MOB-05 Menu and Draft editor
- Approved: yes
- UI: verified (fixtures) | Integration: not verified | Device validation: n/a
- UI evidence: PR #142 squash-merged as f052a59; `verify.ps1` PASS on
  ff25b21 (249 Mobile tests, including the D08.4 rule and basket tests);
  merged content identical to ff25b21. D19.1 timing in a desktop Chromium
  pane with the fixture dev build: add item p95 13.3 ms (n=60), open options
  p95 24.6 ms (n=30); not measured on a phone. The basket was memory-only.
- Integration needs: CAP-07
- Depends on: MOB-02
- PR: https://github.com/qtaro66/POS/pull/142
- Acceptance: items, SubItems, Toppings, quantity and comments; option-group
  rules enforced on screen (min/max, repeats, per-option limit, counted by
  quantity; D08.4); unavailable and Sold Out options blocked; Draft held behind
  a repository port (in memory until MOB-06); p95 at most 100 ms to add an
  item and 200 ms to open options (D19.1).

### MOB-06 Persistent Draft and outbox
- Approved: yes
- UI: n/a | Integration: n/a | Device validation: not verified
- Depends on: MOB-05
- Persistence acceptance:
  - IndexedDB records bound to `deviceId`, `employeeId`, `tableSessionId`
    and `sessionMasterDataVersion` (D10.5).
  - Survive reload, app close and restart; never memory-only.
  - Request states follow D20.1; outbox counts feed the sync status.
  - Unsent Drafts become `Expired/NeedsReview` after 30 days (NFR-06);
    nothing is deleted automatically (DEC-6).
- Device: data survives app close and device restart on Android and iOS,
  including installed-PWA storage behavior.

### MOB-07 Table session detail (read-only)
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-10
- Depends on: MOB-04
- Acceptance: lines and Branch totals, labelled as an estimate while
  `Ordering`; refresh when `tableVersion` changes; never shows payment
  lines, tendered amount, change or card charge (D10.1).

### MOB-08 SubmitOrder and status query
- Approved: no
- UI: not verified | Integration: not verified | Device validation: not verified
- Integration needs: CAP-08, CAP-09
- Depends on: MOB-05, MOB-06, MOB-07
- Acceptance: one Draft sent as one batch; `requestId` created once when
  sending starts and reused; status query before any resend; `Unknown`
  until a query answers; Draft kept on rejection (including
  `PaymentPending`) with lines to fix; resend on resume without background
  work (3.3, D12.2, D20.1). While the Branch is unreachable the order may be
  confirmed as saved on this device (`Pending`), but it is shown as accepted
  only after a `BranchAccepted` result.
- Persistence: outbox state survives a restart mid-send.
- Device: Wi-Fi loss after send and app kill during send on both systems.

### MOB-09 RequestPayment
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-09, CAP-11
- Depends on: MOB-08
- Acceptance: blocked while this device holds an unsent Draft for the
  session; idempotent; `Unknown` resolved by status query; shows the latest
  table status (D10.2).

### MOB-10 Cancellation request
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-09, CAP-12
- Depends on: MOB-07
- Acceptance: choose accepted lines and quantities within the unlocked
  amount; optional reason; atomic; status visible; no withdraw or confirm
  (D10.3).

### MOB-11 Draft ownership and review
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-14
- Depends on: MOB-06
- Acceptance:
  - Unsent count shown on logout; logout is never blocked (D10.5).
  - The same employee resumes the Draft on the same device; another Active
    employee takes it over only after explicit confirmation, recording the
    original creator and the new owner (D10.5).
  - Requests that have started sending keep the original sender's
    `employeeId` and cannot change owner (D10.5).
  - A Draft becomes `Blocked/NeedsReview` and is not retried into its
    original session when that session is `PaymentPending`, `Completed`,
    `Cancelled` or `Closed` (D10.5), or has been replaced by a new session
    (D09). Whether it may be resubmitted after a `PaymentPending` session
    returns to `Ordering` waits for DEC-7.
  - Copying a blocked Draft into a new session needs user confirmation and
    full revalidation of price, options and sellability (D10.5).
  - The Draft is never presented as an order the Branch accepted (D10.5).
  - Re-pairing recovery waits for DEC-2; deletion after
    `Expired/NeedsReview` waits for DEC-6.

### MOB-12 Sold Out for permitted employees
- Approved: no
- UI: not verified | Integration: not verified | Device validation: n/a
- Integration needs: CAP-13
- Depends on: MOB-05
- Acceptance: set or clear only with `SetSoldOut`; read-only otherwise (D08.2).

### MOB-13 Camera QR scanning
- Approved: no
- UI: not verified | Integration: n/a | Device validation: not verified
- Depends on: DEC-3
- Acceptance: scans the pairing QR on Android Chrome and iOS Safari; denied
  or missing camera is explained; payload still parsed by `pairingQr.ts`.

### MOB-14 Installable PWA
- Approved: no
- UI: not verified | Integration: not verified | Device validation: not verified
- Integration needs: CAP-01
- Depends on: MOB-02
- Acceptance: manifest and a hand-written service worker caching the app
  shell only (no API data or totals); visible update prompt; HTTPS only.
- Device: install and update on Android and iOS with the private Root CA.

### MOB-15 Generated client and contract tests
- Approved: no
- UI: n/a | Integration: not verified | Device validation: n/a
- Integration needs: CAP-01
- Depends on: CAP-01
- Acceptance: OpenAPI-generated TypeScript client replaces the hand-typed
  contracts; one contract test suite for both systems (D17).

### MOB-16 Real-device validation campaign
- Approved: no
- UI: n/a | Integration: n/a | Device validation: not verified
- Depends on: DEC-4, hardware
- Acceptance: at least one real device per system (D17); Root CA install and
  removal; Phase 2 Mobile failure cases; up to 20 concurrent devices (NFR-01).

### MOB-17 Production stack evidence
- Approved: no
- UI: n/a | Integration: n/a | Device validation: not verified
- Depends on: MOB-16
- Acceptance: written comparison of installable PWA, cross-platform and
  native against the D17 criteria, with measured results, for DEC-5.
