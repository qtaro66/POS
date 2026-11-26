import { isClosingReportDate } from './closingReportWorkspaceContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'
import type { StoreBillTotals } from './storeLifecycleContracts.ts'

export interface CashHistoryQuery { businessDate?: string; receiptNumber?: string; tableSessionId?: string; tableId?: string; orderBatchId?: string; paymentMethod?: 'All' | 'Cash'; status?: 'All' | 'Completed'; limit?: number; cursor?: string }
export interface CashHistoryItem {
  requestId: string; tableSessionId: string; receiptNumber: string; businessDate: string; storeSessionId: string; shiftId: string
  employeeId: string; deviceId: string; capturedAt: string; paymentMethod: 'Cash'; status: 'Completed'
  tenderedAmount: number; appliedAmount: number; changeAmount: number; capturedPrintStatus: 'QueuedForSimulation'
  tableId?: string | null; orderBatchIds?: string[] | null
}
export interface CashHistoryPage { schemaVersion: 1; branchId: string; asOf: string; businessDate: string; items: CashHistoryItem[]; nextCursor: string | null }
export interface CashHistoryDiscount { kind: 'Percentage' | 'FixedAmount'; requestedValue: number; requestedAmount: number; appliedAmount: number }
export interface CashHistoryBillOption { optionId: string; displayName: string; quantityPerItem: number; unitPrice: number }
export interface CashHistoryBillLine { orderLineId: string; orderBatchId: string; sellableItemId: string; displayName: string; quantity: number; unitPrice: number; fulfillmentType: 'DineIn' | 'Takeaway'; comment: string | null; options: CashHistoryBillOption[]; grossAmount: number; discount: CashHistoryDiscount | null; netAmount: number }
export interface CashHistoryBill { schemaVersion: 1; branchId: string; tableSessionId: string; tableId: string; paymentRequestId: string; capturedAt: string; masterDataVersion: string; calculationPolicyVersion: string; vatRate: number; serviceChargeRate: number; items: CashHistoryBillLine[]; billDiscount: CashHistoryDiscount | null }
export interface CashHistoryDetail { schemaVersion: 1; branchId: string; asOf: string; transaction: CashHistoryItem; financials: StoreBillTotals | null; bill?: CashHistoryBill | null }
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
    (query.tableId !== undefined && !text(query.tableId)) || (query.orderBatchId !== undefined && !text(query.orderBatchId)) ||
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
  const result: CashHistoryItem = { requestId: value.requestId as string, tableSessionId: value.tableSessionId as string, receiptNumber: value.receiptNumber,
    businessDate: value.businessDate, storeSessionId: value.storeSessionId as string, shiftId: value.shiftId as string,
    employeeId: value.employeeId, deviceId: value.deviceId, capturedAt: value.capturedAt, paymentMethod: 'Cash', status: 'Completed',
    tenderedAmount: value.tenderedAmount, appliedAmount: value.appliedAmount, changeAmount: value.changeAmount, capturedPrintStatus: 'QueuedForSimulation' }
  if (Object.hasOwn(value, 'tableId')) {
    if (value.tableId !== null && !text(value.tableId)) invalid()
    result.tableId = value.tableId as string | null
  }
  if (Object.hasOwn(value, 'orderBatchIds')) {
    if (value.orderBatchIds !== null && (!Array.isArray(value.orderBatchIds) || !value.orderBatchIds.every(id => text(id)) || new Set(value.orderBatchIds).size !== value.orderBatchIds.length)) invalid()
    result.orderBatchIds = value.orderBatchIds as string[] | null
  }
  return result
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
    (query.tableSessionId !== undefined && item.tableSessionId !== query.tableSessionId) ||
    (query.tableId !== undefined && item.tableId !== query.tableId) || (query.orderBatchId !== undefined && !item.orderBatchIds?.includes(query.orderBatchId)))) invalid()
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
  const result: CashHistoryDetail = { schemaVersion: 1, branchId, asOf: value.asOf, transaction, financials }
  if (Object.hasOwn(value, 'bill')) {
    if (value.bill === null && transaction.orderBatchIds !== undefined && transaction.orderBatchIds !== null) invalid()
    result.bill = value.bill === null ? null : parseCashHistoryBill(value.bill, branchId, transaction, financials)
  }
  else if (transaction.orderBatchIds !== undefined && transaction.orderBatchIds !== null) invalid()
  return result
}

const positiveInteger = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) > 0 && (value as number) <= 2147483647
const comment = (value: unknown): value is string | null => value === null || typeof value === 'string' && value.length <= 4096 &&
  !Array.from(value).some(character => { const code = character.charCodeAt(0); return code < 32 && code !== 9 && code !== 10 && code !== 13 || code >= 127 && code <= 159 })
const humanName = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value === value.trim() && value.length <= 512 && comment(value)
function parseDiscount(value: unknown, base: number): CashHistoryDiscount | null {
  if (value === null) return null
  if (!object(value) || !['Percentage', 'FixedAmount'].includes(value.kind as string) || !amount(value.requestedValue) ||
    !amount(value.requestedAmount) || !amount(value.appliedAmount) || value.kind === 'Percentage' && value.requestedValue > 100 ||
    value.appliedAmount > value.requestedAmount || value.appliedAmount > base) invalid()
  return { kind: value.kind as CashHistoryDiscount['kind'], requestedValue: value.requestedValue, requestedAmount: value.requestedAmount, appliedAmount: value.appliedAmount }
}
function parseLine(value: unknown): CashHistoryBillLine {
  if (!object(value) || ![value.orderLineId, value.orderBatchId, value.sellableItemId].every(id => text(id)) ||
    !humanName(value.displayName) || !positiveInteger(value.quantity) || !amount(value.unitPrice) ||
    !['DineIn', 'Takeaway'].includes(value.fulfillmentType as string) || !comment(value.comment) ||
    !Array.isArray(value.options) || !amount(value.grossAmount) || !amount(value.netAmount)) invalid()
  const options = value.options.map(raw => {
    if (!object(raw) || !text(raw.optionId) || !humanName(raw.displayName) || !positiveInteger(raw.quantityPerItem) || !amount(raw.unitPrice)) invalid()
    return { optionId: raw.optionId, displayName: raw.displayName, quantityPerItem: raw.quantityPerItem, unitPrice: raw.unitPrice }
  })
  const discount = parseDiscount(value.discount, value.grossAmount)
  if (!equalAmount(value.grossAmount - (discount?.appliedAmount ?? 0), value.netAmount)) invalid()
  return { orderLineId: value.orderLineId as string, orderBatchId: value.orderBatchId as string, sellableItemId: value.sellableItemId as string,
    displayName: value.displayName, quantity: value.quantity, unitPrice: value.unitPrice, fulfillmentType: value.fulfillmentType as CashHistoryBillLine['fulfillmentType'],
    comment: value.comment, options, grossAmount: value.grossAmount, discount, netAmount: value.netAmount }
}
function parseCashHistoryBill(value: unknown, branchId: string, transaction: CashHistoryItem, financials: StoreBillTotals | null): CashHistoryBill {
  if (!object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || value.tableSessionId !== transaction.tableSessionId ||
    !text(value.tableId) || !text(value.paymentRequestId) || !isUtcTimestamp(value.capturedAt) ||
    !text(value.masterDataVersion) || !text(value.calculationPolicyVersion) || !amount(value.vatRate) || !amount(value.serviceChargeRate) || !Array.isArray(value.items) || !financials) invalid()
  const items = value.items.map(parseLine)
  const batches = [...new Set(items.map(item => item.orderBatchId))]
  if (new Set(items.map(item => item.orderLineId)).size !== items.length ||
    transaction.tableId === undefined || transaction.tableId !== value.tableId || !transaction.orderBatchIds ||
    batches.length !== transaction.orderBatchIds.length || batches.some(id => !transaction.orderBatchIds!.includes(id))) invalid()
  const sum = (read: (item: CashHistoryBillLine) => number) => items.reduce((total, item) => total + read(item), 0)
  const billDiscount = parseDiscount(value.billDiscount, sum(item => item.netAmount))
  if (!equalAmount(sum(item => item.grossAmount), financials.itemGrossAmount) ||
    !equalAmount(sum(item => item.discount?.appliedAmount ?? 0), financials.lineDiscountAmount) ||
    !equalAmount(sum(item => item.netAmount), financials.itemGrossAmount - financials.lineDiscountAmount) ||
    !equalAmount(billDiscount?.appliedAmount ?? 0, financials.billDiscountAmount)) invalid()
  return { schemaVersion: 1, branchId, tableSessionId: transaction.tableSessionId, tableId: value.tableId, paymentRequestId: value.paymentRequestId,
    capturedAt: value.capturedAt, masterDataVersion: value.masterDataVersion, calculationPolicyVersion: value.calculationPolicyVersion,
    vatRate: value.vatRate, serviceChargeRate: value.serviceChargeRate, items, billDiscount }
}
