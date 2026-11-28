# PC POS kitchen reprint workspace

Continues 0215 and 0216 with the PC POS history workflow for D01.4.1 and
D18.2-D18.3. The Branch remains the authorization and SQLite writer boundary.
This slice changes no database schema, permission assignment or print worker.

## One original line per explicit command

Select an original production snapshot from captured cash history and a single
submitted line. The original print evidence must be available and agree with
the selected snapshot. The form shows frozen source information and explains
that the new document is a copy for the original production station. It does
not use the chargeable bill to reconstruct cancelled or changed original lines.

The locked action does not grant permission to the logged-in employee. Each
explicit submission requests a fresh authorized employee PIN and reason. The
Branch can accept a different active approver without changing the login.
PINs remain transient, clear when submitted or the form closes, and never enter
the recovery journal, browser report or an authorization capability token.
Explicit labels keep the reason and PIN fields accessible after a rejected
attempt, including when the reason already contains text.

## Durable intent before sending

Persist a command UUID and complete immutable target before the POST. Scope
recovery by service origin, Branch, device and logged-in employee. Store neither
PIN nor reason. IndexedDB read/write transactions enforce one unresolved intent
per scope across tabs. Storage failure or corrupt intent blocks another send.
The caller must await the durable write before invoking the API.

A response must validate Branch, command UUID, target references and derived
copy snapshot/job identities before it can establish acceptance. Transport
failure, a malformed response or an indeterminate server result leaves intent
pending. Reopening or reloading only reads status; it never replays a POST.
A status 404 alone is insufficient evidence to forget a possibly accepted
command. Definitive command rejection can release the intent for a new explicit
attempt, which again requires a fresh PIN. UI ownership invalidates late
responses after authenticated scope or client changes. Leaving the selected
target closes its confirmation; durable pending intent remains recoverable.

## Print outcomes and boundaries

Show queue and observed job status separately from physical paper confirmation.
PrintDispatched records sending; Unknown remains uncertain and never triggers
an automatic resend or a new UUID. Original order, cancellation, stock and cash
records remain unchanged. The copy has its own linked job and existing Branch
audit/attempt trail.

Automated browser acceptance uses an isolated local HTTPS Branch fixture and
no printer configuration or worker capable of contacting hardware. Assertions
inspect SQLite read only; all business changes go through the real Branch API.
Existing history read-only regression continues to check that browsing creates
no writes. Unit/component tests cover protocol corruption, intent ownership,
storage failure, rejected PINs, stale responses and recovery without resending.

D21.3 explicit approval is required before merge for the printing/security
workflow. Physical printer acceptance still requires operator confirmation at
execution time and is outside this hardware-free handoff.
