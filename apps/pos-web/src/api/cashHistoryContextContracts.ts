import { isCashHistoryId, type CashHistoryItem } from './cashHistoryContracts.ts'
import { isClosingReportDate } from './closingReportWorkspaceContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'

interface Subject { schemaVersion: 1; branchId: string; asOf: string; cashRequestId: string; tableSessionId: string }
export interface CashHistoryShiftClosing { closesStore: boolean; expectedCashAmount: number; actualCashAmount: number | null; cashDifference: number | null }
export interface CashHistoryShiftEntry { requestId: string; action: 'OpenStore' | 'OpenShift' | 'CloseShift' | 'CloseStore'; occurredAt: string; branchLocalDate: string; employeeId: string; deviceId: string; cashAmount: number | null; closing: CashHistoryShiftClosing | null }
export interface CashHistoryShiftContext extends Subject { storeSessionId: string; shiftId: string; businessDate: string; entries: CashHistoryShiftEntry[] }
export interface CashHistoryReceiptSimulation { status: 'Simulated' | 'Failed'; text: string | null }
export interface CashHistoryReceiptEvidence extends Subject { receiptNumber: string; queuedAt: string; capturedPrintStatus: 'QueuedForSimulation'; simulation: CashHistoryReceiptSimulation | null }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const amount = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && v >= 0 && v <= Number.MAX_SAFE_INTEGER / 100
const signedAmount = (v: unknown): v is number => typeof v === 'number' && Number.isFinite(v) && Math.abs(v) <= Number.MAX_SAFE_INTEGER / 100
const coherentDifference = (actual: number, expected: number, difference: number) => Math.abs((actual - expected) - difference) <= Math.max(0.0000001, 2 * Number.EPSILON * (Math.abs(actual) + Math.abs(expected) + Math.abs(difference)))
const utcInstant = (value: string) => value.replace(/\+00:00$/, 'Z').replace(/(?:\.(\d{1,7}))?Z$/, (_match, fraction: string | undefined) => '.' + (fraction ?? '').padEnd(7, '0') + 'Z')
function invalid(): never { throw new Error('Invalid captured cash context.') }
function subject(v: unknown, branchId: string, requestId: string): Subject {
  if (!object(v) || v.schemaVersion !== 1 || v.branchId !== branchId || v.cashRequestId !== requestId || !isCashHistoryId(v.tableSessionId) || !isUtcTimestamp(v.asOf)) invalid()
  return { schemaVersion: 1, branchId, cashRequestId: requestId, tableSessionId: v.tableSessionId, asOf: v.asOf }
}
function closing(v: unknown): CashHistoryShiftClosing | null {
  if (v === null) return null
  if (!object(v) || typeof v.closesStore !== 'boolean' || !amount(v.expectedCashAmount) || !(v.actualCashAmount === null || amount(v.actualCashAmount)) || !(v.cashDifference === null || signedAmount(v.cashDifference)) || (v.actualCashAmount === null) !== (v.cashDifference === null) || v.actualCashAmount !== null && !coherentDifference(v.actualCashAmount, v.expectedCashAmount, v.cashDifference as number)) invalid()
  return { closesStore: v.closesStore, expectedCashAmount: v.expectedCashAmount, actualCashAmount: v.actualCashAmount, cashDifference: v.cashDifference }
}
export function parseCashHistoryShiftContext(v: unknown, branch: string, request: string, captured?: CashHistoryItem): CashHistoryShiftContext {
  const s = subject(v, branch, request)
  if (!object(v) || !isCashHistoryId(v.storeSessionId) || !isCashHistoryId(v.shiftId) || !isClosingReportDate(v.businessDate) || !Array.isArray(v.entries) || v.entries.length > 2 || captured && (s.tableSessionId !== captured.tableSessionId || v.storeSessionId !== captured.storeSessionId || v.shiftId !== captured.shiftId || v.businessDate !== captured.businessDate)) invalid()
  const entries = v.entries.map(raw => {
    if (!object(raw) || !isCashHistoryId(raw.requestId) || !['OpenStore', 'OpenShift', 'CloseShift', 'CloseStore'].includes(raw.action as string) || !isUtcTimestamp(raw.occurredAt) || !isClosingReportDate(raw.branchLocalDate) || !isCashHistoryId(raw.employeeId) || !isCashHistoryId(raw.deviceId) || !(raw.cashAmount === null || amount(raw.cashAmount))) invalid()
    const c = closing(raw.closing), isClose = raw.action === 'CloseShift' || raw.action === 'CloseStore'
    if (isClose !== (c !== null) || c && (c.closesStore !== (raw.action === 'CloseStore') || raw.cashAmount !== c.actualCashAmount) || (raw.action === 'OpenStore' || raw.action === 'CloseStore') && raw.cashAmount === null || raw.action === 'OpenStore' && raw.branchLocalDate !== v.businessDate) invalid()
    return { requestId: raw.requestId, action: raw.action as CashHistoryShiftEntry['action'], occurredAt: raw.occurredAt, branchLocalDate: raw.branchLocalDate, employeeId: raw.employeeId, deviceId: raw.deviceId, cashAmount: raw.cashAmount, closing: c }
  })
  if (new Set(entries.map(entry => entry.requestId)).size !== entries.length || entries.length === 2 && (!(entries[0].action === 'OpenStore' || entries[0].action === 'OpenShift') || !(entries[1].action === 'CloseShift' || entries[1].action === 'CloseStore'))) invalid()
  return { ...s, storeSessionId: v.storeSessionId, shiftId: v.shiftId, businessDate: v.businessDate, entries }
}
export function parseCashHistoryReceiptEvidence(v: unknown, branch: string, request: string, captured?: CashHistoryItem): CashHistoryReceiptEvidence {
  const s = subject(v, branch, request)
  if (!object(v) || !isCashHistoryId(v.receiptNumber) || !isUtcTimestamp(v.queuedAt) || v.capturedPrintStatus !== 'QueuedForSimulation' || captured && (s.tableSessionId !== captured.tableSessionId || v.receiptNumber !== captured.receiptNumber || utcInstant(v.queuedAt) !== utcInstant(captured.capturedAt) || v.capturedPrintStatus !== captured.capturedPrintStatus)) invalid()
  let simulation: CashHistoryReceiptSimulation | null = null
  if (v.simulation !== null) {
    const raw = v.simulation
    if (!object(raw) || !['Simulated', 'Failed'].includes(raw.status as string) || raw.status === 'Failed' && raw.text !== null || raw.status === 'Simulated' && (typeof raw.text !== 'string' || raw.text.trim().length === 0 || new TextEncoder().encode(raw.text).length > 128 * 1024 || Array.from(raw.text).some(c => { const n = c.charCodeAt(0); return (n < 32 || n >= 127 && n <= 159) && !['\r', '\n', '\t'].includes(c) }))) invalid()
    simulation = { status: raw.status as 'Simulated' | 'Failed', text: raw.text as string | null }
  }
  return { ...s, receiptNumber: v.receiptNumber, queuedAt: v.queuedAt, capturedPrintStatus: 'QueuedForSimulation', simulation }
}
