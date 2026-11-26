import type { CashHistoryEvent, CashHistoryEventPage, CashHistoryEventSource } from './cashHistoryEventContracts.ts'
import { historyTime } from './cashHistoryTestFixtures.ts'
export function sessionEvidence(id = 'audit-1'): CashHistoryEvent {
  return { eventId: 'session:' + id, source: 'Session', requestId: 'OPEN-1', occurredAt: historyTime, employeeId: 'recorded-employee', deviceId: 'recorded-device', cancellation: null,
    session: { action: 'ChangeTable', previousStatus: 'Ordering', currentStatus: 'Ordering', previousTableId: 'A01', currentTableId: 'A02', tableVersion: 2, reason: 'เหตุผลเดิม\nบรรทัดสอง' } }
}
export function cancellationEvidence(id = 'CANCEL-1'): CashHistoryEvent {
  return { eventId: 'cancellation:' + id, source: 'Cancellation', requestId: id, occurredAt: historyTime, employeeId: 'recorded-confirmer', deviceId: null, session: null,
    cancellation: { outcome: 'PartiallyConfirmed', lines: [{ orderLineId: 'CANCELLED-LINE', checked: { notStarted: 2, inProgress: 1, completed: 1, notApplicable: 0 }, cancelled: { notStarted: 1, inProgress: 0, completed: 0, notApplicable: 0 }, requestedQuantity: 4, notCancelledQuantity: 3 }] } }
}
export const eventPage = (source: CashHistoryEventSource = 'Session', events: CashHistoryEvent[] = [source === 'Session' ? sessionEvidence() : cancellationEvidence()], nextCursor: string | null = null): CashHistoryEventPage =>
  ({ schemaVersion: 1, branchId: 'branch', asOf: historyTime, cashRequestId: 'CASH-1', tableSessionId: 'SESSION-1', source, events, nextCursor })
