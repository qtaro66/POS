import { isUtcTimestamp } from './printAttentionContracts.ts'
import { parseStoreCashSummary, type StoreCashSummary, type StoreLifecycleState } from './storeLifecycleContracts.ts'

export type ClosingReportFilter = 'All' | 'Pending' | 'Delivered'
export interface ClosingReportQuery { status?: ClosingReportFilter; storeSessionId?: string; shiftId?: string; businessDate?: string; limit?: number; cursor?: string }
export type ClosingReportState = 'Queued' | 'InFlight' | 'RetryScheduled' | 'LeaseExpired' | 'Delivered'
export type ClosingReportFailure = 'TransportUnavailable' | 'CentralRejected' | 'AcknowledgmentUnavailable'
export interface ClosingReportItem {
  requestId: string; reportId: string; storeSessionId: string; shiftId: string; businessDate: string
  operation: 'CloseShift' | 'CloseStore'; createdAt: string; deliveryState: ClosingReportState; attemptCount: number
  availableAt: string; leaseExpiresAt: string | null; deliveredAt: string | null; lastFailure: ClosingReportFailure | null
}
export interface ClosingReportPage { branchId: string; asOf: string; items: ClosingReportItem[]; nextCursor: string | null }
export interface ClosingReportDelivery { branchId: string; asOf: string; deliveryEnabled: boolean }
export interface ClosingReportDetail {
  branchId: string; report: ClosingReportItem
  snapshot: { schemaVersion: 1; branchId: string; reportId: string; requestId: string; storeSessionId: string; shiftId: string
    operation: 'CloseShift' | 'CloseStore'; businessDate: string; closing: NonNullable<StoreLifecycleState['closing']>; cashSummary: StoreCashSummary | null }
}
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
export const isClosingReportId = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value)
export const isClosingReportDate = (value: unknown): value is string => typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value) && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 10) === value
const cursor = (value: unknown): value is string => typeof value === 'string' && /^[A-Za-z0-9_-]{1,4096}$/.test(value)
const nullableTime = (value: unknown): value is string | null => value === null || isUtcTimestamp(value)
const text = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 256 && value === value.trim() && !Array.from(value).some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127)
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value) && Number.isSafeInteger(Math.round(value * 100)) && Math.round(value * 100) / 100 === value
function utcOrder(value: string): string {
  const parts = /^(.*T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)!
  return parts[1] + '.' + (parts[2] ?? '').padEnd(7, '0')
}
function invalid(): never { throw new Error('Invalid closing report response.') }
function identity(branchId: string, requestId: string) {
  const hex = (value: string) => Array.from(new TextEncoder().encode(value), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase()
  return 'closing:' + hex(branchId) + ':' + hex(requestId)
}
export function validateClosingReportQuery(query: ClosingReportQuery): void {
  if (!['All', 'Pending', 'Delivered'].includes(query.status ?? 'All') ||
    (query.storeSessionId !== undefined && !isClosingReportId(query.storeSessionId)) ||
    (query.shiftId !== undefined && !isClosingReportId(query.shiftId)) ||
    (query.businessDate !== undefined && !isClosingReportDate(query.businessDate)) ||
    !Number.isSafeInteger(query.limit ?? 20) || (query.limit ?? 20) < 1 || (query.limit ?? 20) > 50 ||
    (query.cursor !== undefined && !cursor(query.cursor))) invalid()
}
export function parseClosingReportItem(value: unknown, branchId: string, asOf?: string): ClosingReportItem {
  if (!object(value) || !isClosingReportId(value.requestId) || !isClosingReportId(value.storeSessionId) || !isClosingReportId(value.shiftId) ||
    value.reportId !== identity(branchId, value.requestId) || !isClosingReportDate(value.businessDate) ||
    !['CloseShift', 'CloseStore'].includes(value.operation as string) || !isUtcTimestamp(value.createdAt) || !isUtcTimestamp(value.availableAt) ||
    !nullableTime(value.leaseExpiresAt) || !nullableTime(value.deliveredAt) ||
    !['Queued', 'InFlight', 'RetryScheduled', 'LeaseExpired', 'Delivered'].includes(value.deliveryState as string) ||
    !Number.isSafeInteger(value.attemptCount) || (value.attemptCount as number) < 0 ||
    !(value.lastFailure === null || ['TransportUnavailable', 'CentralRejected', 'AcknowledgmentUnavailable'].includes(value.lastFailure as string))) invalid()
  const leased = value.deliveryState === 'InFlight' || value.deliveryState === 'LeaseExpired'
  if ((value.deliveryState === 'Delivered') !== (value.deliveredAt !== null) || leased !== (value.leaseExpiresAt !== null) ||
    ((leased || value.deliveryState === 'Delivered' || value.deliveryState === 'RetryScheduled') && value.attemptCount === 0) ||
    (value.deliveryState === 'Delivered' && value.lastFailure !== null) ||
    (value.attemptCount === 0 && value.lastFailure !== null)) invalid()
  if (asOf && ((value.deliveryState === 'InFlight' && utcOrder(value.leaseExpiresAt as string) <= utcOrder(asOf)) ||
    (value.deliveryState === 'LeaseExpired' && utcOrder(value.leaseExpiresAt as string) > utcOrder(asOf)) ||
    (value.deliveryState === 'RetryScheduled' && utcOrder(value.availableAt) <= utcOrder(asOf)))) invalid()
  return { requestId: value.requestId, reportId: value.reportId as string, storeSessionId: value.storeSessionId,
    shiftId: value.shiftId, businessDate: value.businessDate, operation: value.operation as ClosingReportItem['operation'],
    createdAt: value.createdAt, deliveryState: value.deliveryState as ClosingReportState, attemptCount: value.attemptCount as number,
    availableAt: value.availableAt, leaseExpiresAt: value.leaseExpiresAt, deliveredAt: value.deliveredAt, lastFailure: value.lastFailure as ClosingReportFailure | null }
}
export function parseClosingReportPage(value: unknown, branchId: string, query: ClosingReportQuery): ClosingReportPage {
  validateClosingReportQuery(query)
  if (!object(value) || value.branchId !== branchId || !isUtcTimestamp(value.asOf) || !Array.isArray(value.items) ||
    value.items.length > (query.limit ?? 20) || !(value.nextCursor === null || cursor(value.nextCursor)) ||
    (value.nextCursor !== null && value.items.length === 0)) invalid()
  const items = value.items.map(item => parseClosingReportItem(item, branchId, value.asOf as string))
  if (new Set(items.map(item => item.requestId)).size !== items.length || items.some(item =>
    (query.status === 'Pending' && item.deliveryState === 'Delivered') || (query.status === 'Delivered' && item.deliveryState !== 'Delivered') ||
    (query.storeSessionId !== undefined && item.storeSessionId !== query.storeSessionId) ||
    (query.shiftId !== undefined && item.shiftId !== query.shiftId) || (query.businessDate !== undefined && item.businessDate !== query.businessDate))) invalid()
  return { branchId, asOf: value.asOf, items, nextCursor: value.nextCursor }
}
export function parseClosingReportDelivery(value: unknown, branchId: string): ClosingReportDelivery {
  if (!object(value) || value.branchId !== branchId || !isUtcTimestamp(value.asOf) || typeof value.deliveryEnabled !== 'boolean') invalid()
  return { branchId, asOf: value.asOf, deliveryEnabled: value.deliveryEnabled }
}
export function parseClosingReportDetail(value: unknown, branchId: string, requestId: string): ClosingReportDetail {
  if (!object(value) || value.branchId !== branchId) invalid()
  const report = parseClosingReportItem(value.report, branchId)
  const saved = value.snapshot
  if (report.requestId !== requestId || !object(saved) || saved.schemaVersion !== 1 || saved.branchId !== branchId ||
    saved.reportId !== report.reportId || saved.requestId !== requestId || saved.storeSessionId !== report.storeSessionId ||
    saved.shiftId !== report.shiftId || saved.operation !== report.operation || saved.businessDate !== report.businessDate || !object(saved.closing)) invalid()
  const close = saved.closing
  if (close.storeSessionId !== report.storeSessionId || close.shiftId !== report.shiftId || close.closesStore !== (report.operation === 'CloseStore') ||
    !money(close.expectedCashAmount) || close.expectedCashAmount < 0 ||
    !(close.actualCashAmount === null || money(close.actualCashAmount) && close.actualCashAmount >= 0) ||
    !(close.cashDifference === null || money(close.cashDifference)) ||
    (report.operation === 'CloseStore' && close.actualCashAmount === null) ||
    ((close.actualCashAmount === null) !== (close.cashDifference === null)) ||
    (close.actualCashAmount !== null && Math.round((close.actualCashAmount as number) * 100) - Math.round(close.expectedCashAmount * 100) !== Math.round((close.cashDifference as number) * 100)) ||
    close.reconciliationStatus !== (close.actualCashAmount === null ? 'NotReconciled' : 'Reconciled') || !object(close.context) ||
    !text(close.context.employeeId) || !text(close.context.deviceId) || !isUtcTimestamp(close.context.occurredAt) || !isClosingReportDate(close.context.branchLocalDate)) invalid()
  const context = close.context
  const closing: NonNullable<StoreLifecycleState['closing']> = { storeSessionId: report.storeSessionId, shiftId: report.shiftId,
    closesStore: close.closesStore as boolean, expectedCashAmount: close.expectedCashAmount,
    actualCashAmount: close.actualCashAmount as number | null, cashDifference: close.cashDifference as number | null,
    reconciliationStatus: close.reconciliationStatus as string,
    context: { employeeId: context.employeeId as string, deviceId: context.deviceId as string, occurredAt: context.occurredAt as string, branchLocalDate: context.branchLocalDate as string } }
  const cashSummary = saved.cashSummary === null ? null : parseStoreCashSummary(saved.cashSummary)
  if (cashSummary) for (const totals of [cashSummary.store, cashSummary.shift]) {
    if (totals && (!money(totals.appliedCashAmount) || totals.financials && Object.values(totals.financials).some(amount => !money(amount)))) invalid()
  }
  return { branchId, report, snapshot: { schemaVersion: 1, branchId, reportId: report.reportId, requestId,
    storeSessionId: report.storeSessionId, shiftId: report.shiftId, operation: report.operation, businessDate: report.businessDate, closing, cashSummary } }
}
