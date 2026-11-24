import { isClosingReportDate } from './closingReportWorkspaceContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'
import type { StoreBillTotals } from './storeLifecycleContracts.ts'

export interface CashHistoryQuery { businessDate?: string; receiptNumber?: string; tableSessionId?: string; paymentMethod?: 'All' | 'Cash'; status?: 'All' | 'Completed'; limit?: number; cursor?: string }
export interface CashHistoryItem {
  requestId: string; tableSessionId: string; receiptNumber: string; businessDate: string; storeSessionId: string; shiftId: string
  employeeId: string; deviceId: string; capturedAt: string; paymentMethod: 'Cash'; status: 'Completed'
  tenderedAmount: number; appliedAmount: number; changeAmount: number; capturedPrintStatus: 'QueuedForSimulation'
}
export interface CashHistoryPage { schemaVersion: 1; branchId: string; asOf: string; businessDate: string; items: CashHistoryItem[]; nextCursor: string | null }
export interface CashHistoryDetail { schemaVersion: 1; branchId: string; asOf: string; transaction: CashHistoryItem; financials: StoreBillTotals | null }
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const text = (value: unknown, max = 256): value is string => typeof value === 'string' && value.length > 0 && value.length <= max && value === value.trim() &&
  !Array.from(value).some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) >= 127 && character.charCodeAt(0) <= 159)
export const isCashHistoryId = (value: unknown): value is string => text(value)
const cursor = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{1,4096}$/.test(value)
const amount = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && value >= 0 && value <= Number.MAX_SAFE_INTEGER / 100
const equalAmount = (left: number, right: number) => Math.abs(left - right) <= 0.0000001
function invalid(): never { throw new Error('Invalid cash history response.') }
export function validateCashHistoryQuery(query: CashHistoryQuery): void {
  if ((query.businessDate !== undefined && !isClosingReportDate(query.businessDate)) ||
    (query.receiptNumber !== undefined && !text(query.receiptNumber, 128)) ||
    (query.tableSessionId !== undefined && !text(query.tableSessionId)) ||
    !['All', 'Cash'].includes(query.paymentMethod === undefined ? 'All' : query.paymentMethod) ||
    !['All', 'Completed'].includes(query.status === undefined ? 'All' : query.status) ||
    (query.limit !== undefined && !Number.isSafeInteger(query.limit)) || (query.limit ?? 20) < 1 || (query.limit ?? 20) > 50 ||
    (query.cursor !== undefined && !cursor(query.cursor))) invalid()
}
export function parseCashHistoryItem(value: unknown): CashHistoryItem {
  if (!object(value) || ![value.requestId, value.tableSessionId, value.storeSessionId, value.shiftId].every(value => text(value)) ||
    !text(value.receiptNumber) || !text(value.employeeId) || !text(value.deviceId) || !isClosingReportDate(value.businessDate) ||
    !isUtcTimestamp(value.capturedAt) || value.paymentMethod !== 'Cash' || value.status !== 'Completed' ||
    value.capturedPrintStatus !== 'QueuedForSimulation' || !amount(value.tenderedAmount) || !amount(value.appliedAmount) ||
    !amount(value.changeAmount) || value.tenderedAmount < value.appliedAmount ||
    !equalAmount(value.tenderedAmount - value.appliedAmount, value.changeAmount)) invalid()
  return { requestId: value.requestId as string, tableSessionId: value.tableSessionId as string, receiptNumber: value.receiptNumber,
    businessDate: value.businessDate, storeSessionId: value.storeSessionId as string, shiftId: value.shiftId as string,
    employeeId: value.employeeId, deviceId: value.deviceId, capturedAt: value.capturedAt, paymentMethod: 'Cash', status: 'Completed',
    tenderedAmount: value.tenderedAmount, appliedAmount: value.appliedAmount, changeAmount: value.changeAmount, capturedPrintStatus: 'QueuedForSimulation' }
}
export function parseCashHistoryPage(value: unknown, branchId: string, query: CashHistoryQuery): CashHistoryPage {
  validateCashHistoryQuery(query)
  if (!object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || !isUtcTimestamp(value.asOf) ||
    !isClosingReportDate(value.businessDate) || (query.businessDate !== undefined && value.businessDate !== query.businessDate) ||
    !Array.isArray(value.items) || value.items.length > (query.limit ?? 20) || !(value.nextCursor === null || cursor(value.nextCursor)) ||
    (value.nextCursor !== null && value.items.length === 0)) invalid()
  const items = value.items.map(parseCashHistoryItem)
  if (new Set(items.map(item => item.requestId)).size !== items.length || items.some(item => item.businessDate !== value.businessDate ||
    (query.receiptNumber !== undefined && !item.receiptNumber.includes(query.receiptNumber)) ||
    (query.tableSessionId !== undefined && item.tableSessionId !== query.tableSessionId))) invalid()
  return { schemaVersion: 1, branchId, asOf: value.asOf, businessDate: value.businessDate, items, nextCursor: value.nextCursor }
}
export function parseCashHistoryDetail(value: unknown, branchId: string, requestId: string): CashHistoryDetail {
  if (!object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || !isUtcTimestamp(value.asOf)) invalid()
  const transaction = parseCashHistoryItem(value.transaction)
  if (transaction.requestId !== requestId) invalid()
  let financials: StoreBillTotals | null = null
  if (value.financials !== null) {
    const fields = ['itemGrossAmount', 'lineDiscountAmount', 'billDiscountAmount', 'serviceChargeAmount', 'totalBeforeRounding', 'roundingAdjustment', 'amountDue', 'vatInclusiveAmount', 'amountBeforeVat'] as const
    const raw = value.financials
    if (!object(raw) || fields.some(key => typeof raw[key] !== 'number' || !Number.isFinite(raw[key]))) invalid()
    const bill = raw as unknown as StoreBillTotals
    if (bill.roundingAdjustment > 0 || fields.some(key => key !== 'roundingAdjustment' && !amount(bill[key])) || !equalAmount(bill.amountDue, transaction.appliedAmount) ||
      !equalAmount(bill.totalBeforeRounding + bill.roundingAdjustment, bill.amountDue) ||
      !equalAmount(bill.itemGrossAmount - bill.lineDiscountAmount - bill.billDiscountAmount + bill.serviceChargeAmount, bill.totalBeforeRounding) ||
      !equalAmount(bill.amountBeforeVat + bill.vatInclusiveAmount, bill.totalBeforeRounding)) invalid()
    financials = Object.fromEntries(fields.map(key => [key, bill[key]])) as unknown as StoreBillTotals
  }
  return { schemaVersion: 1, branchId, asOf: value.asOf, transaction, financials }
}
