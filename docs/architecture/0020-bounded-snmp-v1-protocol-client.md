# ADR 0020: Bounded SNMP v1 protocol client

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.2-D13.3 and D20.3-D20.4
- Extends: ADR 0018 and ADR 0019
- Protocol references: [RFC 1157, SNMP](https://www.rfc-editor.org/rfc/rfc1157.html) and [ITU-T X.690, ASN.1 BER](https://www.itu.int/rec/T-REC-X.690/)

## Decision

`BoundedSnmpV1PrinterClient` implements the typed client boundary from ADR 0019 without owning sockets or configuration storage. It obtains community bytes from `ISnmpV1CommunityProvider`, exchanges one datagram through `ISnmpV1DatagramTransport`, and obtains positive request identifiers from `ISnmpV1RequestIdSource`.

GET uses an RFC 1157 `GetRequest-PDU`. A walk issues sequential `GetNextRequest-PDU` messages, requires every returned OID to advance numerically, stops successfully only after the response leaves the requested subtree, and returns an incomplete result when the caller's bound is reached. The public bound is limited to 1-1,024 variables; the TM-T82X snapshot reader requests at most 256. The client performs no automatic retry.

## Response validation

The internal codec uses definite-length BER and accepts packets no larger than 65,535 bytes. It validates the complete outer message and parent boundaries, SNMP version 1, community bytes using a fixed-time comparison, `GetResponse-PDU`, request ID, zero error status, exactly one variable binding, canonical OID encoding, and the absence of trailing bytes.

Integers and octet strings become typed values. Other primitive values, such as Printer MIB `TimeTicks`, remain defensive byte copies and are never reinterpreted as integers. Constructed values in the variable-value position are rejected. Agent errors retain their error status and index in `SnmpV1AgentException`.

Community bytes are copied immediately after resolution and are never written to logs, exceptions, profiles, or committed configuration. This limits accidental mutation and disclosure but does not make SNMP v1 secure: the protocol provides neither encryption nor cryptographic authentication.

## Safety and scope

Automated tests use fixed RFC-shaped packet fixtures, an in-memory community provider, deterministic request IDs, and a fake datagram transport. They perform no DNS lookup, UDP transmission, printer contact, printing, or cash-drawer operation.

This slice does not implement the UDP transport, timeout policy, community-secret storage, dependency-injection registration, or polling enablement. It cannot infer Cutter Error or provide job-specific acknowledgement or automatic `Completed`.

## Deliberately outside this slice

- bounded UDP request/response transport with endpoint and timeout validation
- secure local community configuration and redaction checks
- runtime composition with the snapshot reader and condition probe
- physical Windows/Linux commissioning of the production path
- Cutter Error mapping pending verified evidence
