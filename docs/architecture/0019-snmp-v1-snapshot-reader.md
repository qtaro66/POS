# ADR 0019: Profile-specific SNMP v1 snapshot reader

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.2-D13.3 and D20.3-D20.4
- Extends: ADR 0017 and ADR 0018
- Protocol reference: [RFC 3805, Printer MIB v2](https://www.rfc-editor.org/rfc/rfc3805.html)

## Decision

`EpsonTmT82xM352ASnapshotReader` converts typed SNMP v1 variables into the `PrinterMibSnapshot` consumed by ADR 0018. It accepts only the ordinal-exact compatibility profile `epson-tm-t82x-m352a-v1`, `StatusMode.SnmpPrinterMib`, and status-command profile `snmp-printer-mib-v1`.

The reader requests the three scalar OIDs physically verified during commissioning:

- cover status: `1.3.6.1.2.1.43.6.1.1.3.1.1`
- paper current level: `1.3.6.1.2.1.43.8.2.1.10.1.1`
- paper status: `1.3.6.1.2.1.43.8.2.1.11.1.1`

It also walks `prtAlertEntry` at `1.3.6.1.2.1.43.18.1.1`, bounded to 256 variables. RFC 3805 defines `prtAlertCode` as column 7 and `prtAlertDescription` as column 8, indexed by `hrDeviceIndex` and `prtAlertIndex`. The reader pairs these columns by both indexes. An empty description is retained because RFC 3805 permits a null description string.

## Fail-closed rules

The alert table is complete only when the client confirms that the walk exited the subtree normally and every observed alert row has exactly one valid non-negative code and one description. A truncated walk, malformed index, duplicate column, wrong value type, or unpaired row sets `AlertTableComplete` to false. Other standard columns are not interpreted, but their row indexes still prove that an alert row exists and therefore must have the required code and description.

Scalar responses must return the exact requested OID and an integer. Unexpected OIDs or types fail the read. Network, timeout, protocol, and cancellation exceptions from the client propagate to `SafePrintDispatcher`, which blocks before creating a Print Attempt.

## Safety and scope

`ISnmpV1PrinterClient` is an injected protocol boundary. Automated tests use an in-memory fake and perform no DNS lookup, UDP transmission, credential read, printer contact, printing, or cash-drawer operation.

This slice does not provide BER encoding/decoding, UDP transport, SNMP community storage, retry policy, dependency-injection registration, or polling enablement. It does not infer Cutter Error and cannot produce job-specific acknowledgement or automatic `Completed`.

## Deliberately outside this slice

- bounded SNMP v1 BER/UDP client
- secure local injection of the SNMP community value
- runtime composition with `SnmpPrinterConditionProbe`
- physical Windows/Linux commissioning of the production path
- Cutter Error mapping pending verified evidence
