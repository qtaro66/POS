# ADR 0017: RAW TCP print transport

- Status: Accepted
- Date: 2026-10-16
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.1-D13.3 and D20.3-D20.4
- Extends: ADR 0010, ADR 0011, ADR 0012, and ADR 0013

## Decision

`RawTcpPrintTransportAdapter` implements `IPrintTransportAdapter` for `RawTcpEscPos`. It connects to the exact host and port from the resolved `PrinterProfile`, enables TCP `NoDelay`, applies the profile's connection and send timeouts, and writes the already-rendered immutable payload without modifying it.

The adapter completes `IPrintSendObserver.OnFirstByteSendingAsync` immediately before the network stream is allowed to write. Connection or DNS failure occurs before this boundary and is therefore eligible for the dispatcher's `Failed` decision. Observer failure prevents the write but remains conservatively `Unknown` because the durable lifecycle marks the attempt before it performs persistence. Write, flush, timeout, cancellation, or disposal failure after the observer boundary is `Unknown` and must not be retried automatically.

The adapter does not interpret a successful socket write as physical completion. A complete send remains `PrintDispatched`; Phase 0 does not produce automatic `Completed`.

## Timeouts and cancellation

Connection and send timeouts use linked cancellation tokens. Expiration of the adapter-owned timeout becomes `TimeoutException`; caller cancellation remains `OperationCanceledException`. `SafePrintDispatcher` still determines the persistent outcome from whether the first-byte boundary was crossed.

## Safety and scope

Automated tests inject fake connections and assert event order, exact payload bytes, timeout forwarding, error boundaries, and disposal. They do not resolve DNS, open a socket, contact a printer, or operate a cash drawer.

This slice does not register the adapter in Branch API dependency injection and does not enable spool polling. Production registration remains blocked until a condition probe is composed alongside the transport.

## Deliberately outside this slice

- SNMP Printer MIB condition probing and Epson TM-T82X status mapping
- Branch API runtime composition and deployment configuration
- physical Windows/Linux printing and timeout commissioning
- job-specific ACK or automatic `Completed`
- IPP and LPR adapters
- cash-drawer commands
