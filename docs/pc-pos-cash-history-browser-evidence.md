# Local Edge cash-history acceptance

Run from the repository root with Microsoft Edge and the existing Playwright
runtime available to Node. The loopback fixture port 17443 must be free. This
does not require a physical printer, drawer or Central service.

```pwsh
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashHistory.ps1 -ListCases
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashHistory.ps1 -SelfTest
pwsh tests/Pos.PcPos.BrowserHost/Test-EdgeCashHistory.ps1 -ReportPath .handoff/history-browser-acceptance.json
```

The runner builds the browser-fixture PC POS bundle and Branch test host once,
then runs the seven selected cases sequentially. Each case owns an isolated
temporary database, a new Edge context and its fixture processes. Reports must
use a new ignored .handoff JSON path. An existing report is never overwritten.

To run a focused subset, select canonical case names:

```pwsh
& ./tests/Pos.PcPos.BrowserHost/Test-EdgeCashHistory.ps1 -Cases Flow,PrintPaging -ReportPath .handoff/history-browser-focused.json
```

Use a clean final commit for handoff evidence. Check that the report commit
equals HEAD, WorkingTreeClean is true, all planned cases were attempted and
passed, and every passed case has observed Edge-version/read-only evidence.
An iteration report from a dirty checkout is useful for diagnosis but cannot
establish acceptance of a final commit.

The report distinguishes build failure, an early stopped failure and a complete
run. ContinueOnFailure can gather remaining results, but any failed or missing
case still makes the command fail. Capture the report together with the normal
tools/verify.ps1 validation; browser acceptance supplements that repository gate.

The seeded scenario and coverage boundaries are described in
architecture/0214-pc-pos-cash-history-browser-acceptance.md. Cash, bill,
cancellation, lifecycle, simulated receipt, original production and attempt
views are included. Read-only fingerprints exclude authentication/session setup
and include business/audit/print/outbox data.

Evidence contains case names, phases, assertion counts, observed browser version
and read-only outcomes. It must not contain PINs, pairing links, tokens, device
identifiers, request headers or browser storage. Local loopback TLS and simulated
print outcomes do not establish deployed or physical hardware acceptance.
