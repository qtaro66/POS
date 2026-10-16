# ADR 0018: SNMP printer-condition mapping boundary

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.2-D13.3 and D20.3-D20.4
- Extends: ADR 0010, ADR 0013, and ADR 0017

## Decision

SNMP transport and model-specific condition interpretation are separate boundaries. `IPrinterMibSnapshotReader` will eventually own configured network reads and produce a typed `PrinterMibSnapshot`. `SnmpPrinterConditionProbe` selects an ordinal, exact compatibility-profile evaluator and returns only the shared `PrinterCondition` used by `SafePrintDispatcher`. The target profile currently uses SNMP v1: its community string is sensitive configuration, but the protocol provides neither cryptographic authentication nor encryption.

Profiles with `StatusMode = Unsupported` return `StatusUnsupported` without reading SNMP. An SNMP profile without an exact registered evaluator returns `Unknown` and blocks dispatch. Reader exceptions propagate to `SafePrintDispatcher`, which records a preflight probe failure and creates no Print Attempt.

## Epson TM-T82X M352A provisional mapping

`EpsonTmT82xM352AConditionEvaluator` applies only to `epson-tm-t82x-m352a-v1` and uses values physically verified during commissioning:

- cover value `3` is `CoverOpen`
- cover value `4` is the required closed state for `Ready`
- paper current level `0` together with paper status `19` is `PaperOut`
- active alert code `808` with the trimmed exact description `Roll Paper End` is `PaperOut`
- paper current level `-3`, paper status `0`, closed cover, a complete alert-table read, and no active alerts is `Ready`

Cover-open takes precedence when cover and paper signals conflict. All unverified, incomplete, contradictory, or unmapped values return `Unknown` and block before sending. No Cutter Error mapping is implemented because commissioning has not verified an OID or alert value; inventing one could allow an unsafe send or misreport another fault.

Snapshots own defensive copies of active alerts so later transport-buffer mutation cannot change a decision.

## Safety and scope

Automated tests use an in-memory snapshot reader. They do not perform DNS lookup, send UDP, read an SNMP community, contact a printer, print paper, or operate a cash drawer.

This slice does not implement SNMP BER encoding/decoding, UDP transport, credential storage, retry/timeout policy, Branch API registration, or polling enablement. The RAW TCP adapter remains unregistered.

## Deliberately outside this slice

- SNMP v1 network reader and secure community configuration
- Cutter Error mapping pending manufacturer-approved commissioning evidence
- runtime dependency-injection composition
- offline/network-failure classification beyond the existing blocked probe-failure rule
- job-specific ACK or automatic `Completed`
