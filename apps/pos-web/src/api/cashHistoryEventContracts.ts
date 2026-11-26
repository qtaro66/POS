import { isCashHistoryId } from './cashHistoryContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'

export type CashHistoryEventSource = 'Session' | 'Cancellation'
export interface CashHistoryEventQuery { source?: CashHistoryEventSource; limit?: number; cursor?: string }
export interface CashHistoryProductionCounts { notStarted: number; inProgress: number; completed: number; notApplicable: number }
export interface CashHistoryCancellationLine { orderLineId: string; checked: CashHistoryProductionCounts; cancelled: CashHistoryProductionCounts; requestedQuantity: number; notCancelledQuantity: number }
export interface CashHistoryCancellationEvent { outcome: 'Confirmed' | 'PartiallyConfirmed'; lines: CashHistoryCancellationLine[] }
export interface CashHistorySessionEvent { action: string; previousStatus: string | null; currentStatus: string; previousTableId: string; currentTableId: string; tableVersion: number; reason: string | null }
export interface CashHistoryEvent { eventId: string; source: CashHistoryEventSource; requestId: string; occurredAt: string; employeeId: string; deviceId: string | null; session: CashHistorySessionEvent | null; cancellation: CashHistoryCancellationEvent | null }
export interface CashHistoryEventPage { schemaVersion: 1; branchId: string; asOf: string; cashRequestId: string; tableSessionId: string; source: CashHistoryEventSource; events: CashHistoryEvent[]; nextCursor: string | null }
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const cursor = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{1,4096}$/.test(value)
const eventId = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 1024 && value === value.trim() && !Array.from(value).some(character => { const code = character.charCodeAt(0); return code < 32 || code >= 127 && code <= 159 })
const count = (value: unknown): value is number => Number.isInteger(value) && (value as number) >= 0 && (value as number) <= 2147483647
const statuses = ['Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid', 'Completed', 'Cancelled', 'Closed']
const actions = ['OpenTable', 'SubmitOrder', 'ChangeTable', 'RequestPayment', 'CancelPaymentRequest', 'StartPayment', 'PaymentFailed', 'CancelPayment', 'ConfirmPartialPayment', 'CompletePayment', 'ReverseAllConfirmedPaymentLines', 'CancelBill', 'CloseTable']
const keys = ['notStarted', 'inProgress', 'completed', 'notApplicable'] as const
function invalid(): never { throw new Error('Invalid cash history evidence.') }
export function validateCashHistoryEventQuery(query: CashHistoryEventQuery): void {
  if (!['Session', 'Cancellation'].includes(query.source === undefined ? 'Session' : query.source) ||
    query.limit !== undefined && (!Number.isInteger(query.limit) || query.limit < 1 || query.limit > 50) ||
    query.cursor !== undefined && !cursor(query.cursor)) invalid()
}
function counts(value: unknown): CashHistoryProductionCounts {
  if (!object(value) || keys.some(key => !count(value[key]))) invalid()
  return { notStarted: value.notStarted as number, inProgress: value.inProgress as number, completed: value.completed as number, notApplicable: value.notApplicable as number }
}
const sum = (value: CashHistoryProductionCounts) => keys.reduce((total, key) => total + value[key], 0)
function cancellation(value: unknown): CashHistoryCancellationEvent {
  if (!object(value) || !['Confirmed', 'PartiallyConfirmed'].includes(value.outcome as string) || !Array.isArray(value.lines) || value.lines.length === 0) invalid()
  const lines = value.lines.map(raw => {
    if (!object(raw) || !isCashHistoryId(raw.orderLineId) || !count(raw.requestedQuantity) || raw.requestedQuantity === 0 || !count(raw.notCancelledQuantity)) invalid()
    const checked = counts(raw.checked), cancelled = counts(raw.cancelled)
    if (sum(checked) !== raw.requestedQuantity || keys.some(key => cancelled[key] > checked[key]) || raw.notCancelledQuantity !== raw.requestedQuantity - sum(cancelled)) invalid()
    return { orderLineId: raw.orderLineId, checked, cancelled, requestedQuantity: raw.requestedQuantity, notCancelledQuantity: raw.notCancelledQuantity }
  })
  if (new Set(lines.map(line => line.orderLineId)).size !== lines.length || !lines.some(line => sum(line.cancelled) > 0) ||
    (value.outcome === 'Confirmed') !== lines.every(line => line.notCancelledQuantity === 0)) invalid()
  return { outcome: value.outcome as CashHistoryCancellationEvent['outcome'], lines }
}
function session(value: unknown): CashHistorySessionEvent {
  if (!object(value) || !actions.includes(value.action as string) || !(value.previousStatus === null || statuses.includes(value.previousStatus as string)) ||
    !statuses.includes(value.currentStatus as string) || !isCashHistoryId(value.previousTableId) || !isCashHistoryId(value.currentTableId) ||
    !Number.isSafeInteger(value.tableVersion) || (value.tableVersion as number) < 1 ||
    !(value.reason === null || typeof value.reason === 'string' && value.reason.length <= 4096 && !Array.from(value.reason).some(character => {
      const code = character.charCodeAt(0); return (code < 32 || code >= 127 && code <= 159) && !['\r', '\n', '\t'].includes(character)
    }))) invalid()
  return { action: value.action as string, previousStatus: value.previousStatus as string | null, currentStatus: value.currentStatus as string,
    previousTableId: value.previousTableId, currentTableId: value.currentTableId, tableVersion: value.tableVersion as number, reason: value.reason as string | null }
}
export function parseCashHistoryEventPage(value: unknown, branchId: string, cashRequestId: string, query: CashHistoryEventQuery, tableSessionId?: string): CashHistoryEventPage {
  validateCashHistoryEventQuery(query)
  const source = query.source ?? 'Session'
  if (!object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || value.cashRequestId !== cashRequestId || !isCashHistoryId(value.tableSessionId) ||
    tableSessionId !== undefined && value.tableSessionId !== tableSessionId || value.source !== source || !isUtcTimestamp(value.asOf) ||
    !Array.isArray(value.events) || value.events.length > (query.limit ?? 20) || !(value.nextCursor === null || cursor(value.nextCursor)) || value.nextCursor !== null && value.events.length === 0) invalid()
  const events = value.events.map(raw => {
    if (!object(raw) || !eventId(raw.eventId) || !isCashHistoryId(raw.requestId) || raw.source !== source || !isUtcTimestamp(raw.occurredAt) || !isCashHistoryId(raw.employeeId)) invalid()
    if (source === 'Session') {
      if (!raw.eventId.startsWith('session:') || raw.eventId.length === 8 || !isCashHistoryId(raw.deviceId) || raw.cancellation !== null) invalid()
      return { eventId: raw.eventId, source, requestId: raw.requestId, occurredAt: raw.occurredAt, employeeId: raw.employeeId, deviceId: raw.deviceId, session: session(raw.session), cancellation: null }
    }
    if (raw.eventId !== 'cancellation:' + raw.requestId || raw.deviceId !== null || raw.session !== null) invalid()
    return { eventId: raw.eventId, source, requestId: raw.requestId, occurredAt: raw.occurredAt, employeeId: raw.employeeId, deviceId: null, session: null, cancellation: cancellation(raw.cancellation) }
  })
  if (new Set(events.map(event => event.eventId)).size !== events.length) invalid()
  return { schemaVersion: 1, branchId, asOf: value.asOf, cashRequestId, tableSessionId: value.tableSessionId, source, events, nextCursor: value.nextCursor }
}
