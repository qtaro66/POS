# ADR 0011: Persistent Print Attempt leases and first-byte recovery

- Status: Accepted
- Date: 2026-10-15
- Requirement source: `docs/requirements/POS-Requirements-v0.2.md`, D13.3 and D20.3-D20.4
- Extends: ADR 0004, ADR 0009, and ADR 0010

## Decision

Branch SQLite owns Print Job leasing and durable Print Attempt history. A worker must acquire a time-limited lease for an exact `printerId` before dispatching. A partial unique index permits at most one active lease per printer, including when more than one Branch Service process contends for work. Terminal attempts are immutable.

Claiming a job does not create a Print Attempt and does not change the job from `PrintQueued`. Unsafe printer preflight therefore releases the lease with `blockedReason` and leaves the attempt count unchanged.

After preflight succeeds, the dispatch lifecycle creates one `Connecting` attempt. Immediately before the transport may send its first byte, the adapter must await the lifecycle journal. That transaction changes both the attempt and job to `Dispatching`. Transport code must not send until this journal call completes successfully.

If the initial `Connecting` journal cannot be established, dispatch does not call the transport and propagates the persistence error for lease recovery; it must not manufacture a transport result for an attempt that may not exist.

Entering the first-byte journal is treated as the conservative uncertainty boundary. If that journal call fails ambiguously, dispatch returns `Unknown` with no automatic retry even though the adapter must not proceed to write bytes.

Terminal transitions are persisted in one transaction:

- failure while `Connecting`: attempt and job become `Failed`; retry is scheduled only when the profile retry limit permits it
- completion while `Dispatching`: attempt and job become `PrintDispatched`
- uncertainty from either `Connecting` or `Dispatching`: attempt and job become `Unknown`; no automatic retry

The same `printAttemptId` and data may repeat Start, first-byte, and terminal calls without creating another attempt. Reuse with different job, printer, worker, or payload data is rejected.

## Lease-expiry recovery

Recovery runs inside an immediate SQLite transaction:

- expired lease with no attempt: release the lease; keep `PrintQueued`
- expired `Connecting` attempt: mark `Failed`; schedule a safe retry only within profile policy
- expired `Dispatching` attempt: mark `Unknown`; never schedule an automatic retry

This intentionally prefers duplicate prevention over optimistic recovery after the first-byte boundary.

## SQLite schema version 9

Schema version 9 adds `assigned_printer_id`, `lease_owner`, and `lease_expires_at` to `print_jobs`, plus:

- `print_attempts` with attempt number, printer, worker, payload SHA-256, first-byte timestamp, terminal result, error, and retry decision
- one-active-lease-per-printer uniqueness
- printer dispatch and Print Attempt lookup indexes

## Safety boundary

Automated tests use fake condition probes and transport adapters. They verify cross-worker lease contention, blocked preflight, first-byte journaling, backoff, retry exhaustion, idempotent transitions, and restart recovery without opening a network socket or contacting a printer.

## Deliberately outside this slice

- hosted polling/background-service loop and shutdown orchestration
- payload renderer and persisted binary spool
- RAW TCP, IPP, or LPR transport implementation
- lease renewal for long-running sends
- manual reprint/resolution workflow for `Unknown`
- physical printer validation
