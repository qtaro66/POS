import { isCashHistoryId } from './cashHistoryContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'
export interface CashHistoryProductionQuery { orderBatchId?: string; orderLineId?: string; productionStationId?: string; limit?: number; cursor?: string }
export interface CashHistoryProductionItem { snapshotId: string; orderBatchId: string; submittedRequestId: string; tableSessionId: string; tableId: string; productionStationId: string; submittedAt: string; lineCount: number; orderLineIds: string[] }
export interface CashHistoryProductionOption { optionGroupId: string | null; displayName: string; quantityPerItem: number }
export interface CashHistoryProductionLine { orderLineId: string; lineSequence: number; displayName: string; quantity: number; fulfillmentType: 'DineIn' | 'Takeaway'; comment: string | null; options: CashHistoryProductionOption[] }
interface Subject { schemaVersion: 1; branchId: string; asOf: string; cashRequestId: string; tableSessionId: string }
export interface CashHistoryProductionPage extends Subject { items: CashHistoryProductionItem[]; nextCursor: string | null }
export interface CashHistoryProductionDetail extends Subject { snapshot: CashHistoryProductionItem; lines: CashHistoryProductionLine[] }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const positive = (v: unknown): v is number => Number.isInteger(v) && (v as number) > 0 && (v as number) <= 2147483647
const cursor = (v: unknown): v is string => typeof v === 'string' && /^[A-Za-z0-9_-]{1,4096}$/.test(v)
export const isProductionSnapshotId = (v: unknown): v is string => typeof v === 'string' && v.length > 0 && v.length <= 1024 && v === v.trim() && !Array.from(v).some(c => { const n = c.charCodeAt(0); return n < 32 || n >= 127 && n <= 159 })
const text = (v: unknown, max: number): v is string => typeof v === 'string' && v.length <= max && !Array.from(v).some(c => { const n = c.charCodeAt(0); return (n < 32 || n >= 127 && n <= 159) && !['\r', '\n', '\t'].includes(c) })
function invalid(): never { throw new Error('Invalid original production snapshot.') }
export function validateCashHistoryProductionQuery(q: CashHistoryProductionQuery) {
  for (const key of ['orderBatchId', 'orderLineId', 'productionStationId'] as const) if (q[key] !== undefined && !isCashHistoryId(q[key])) invalid()
  if (q.limit !== undefined && (!positive(q.limit) || q.limit > 50) || q.cursor !== undefined && !cursor(q.cursor)) invalid()
}
function subject(v: unknown, branchId: string, requestId: string, session?: string): Subject {
  if (!object(v) || v.schemaVersion !== 1 || v.branchId !== branchId || v.cashRequestId !== requestId || !isCashHistoryId(v.tableSessionId) || session !== undefined && v.tableSessionId !== session || !isUtcTimestamp(v.asOf)) invalid()
  return { schemaVersion: 1, branchId, cashRequestId: requestId, tableSessionId: v.tableSessionId, asOf: v.asOf }
}
function item(v: unknown, session: string): CashHistoryProductionItem {
  if (!object(v) || !isProductionSnapshotId(v.snapshotId) || !isCashHistoryId(v.orderBatchId) || !isCashHistoryId(v.submittedRequestId) || v.tableSessionId !== session || !isCashHistoryId(v.tableId) || !isCashHistoryId(v.productionStationId) || !isUtcTimestamp(v.submittedAt) || !positive(v.lineCount) || !Array.isArray(v.orderLineIds) || v.orderLineIds.length !== v.lineCount || !v.orderLineIds.every(isCashHistoryId) || new Set(v.orderLineIds).size !== v.lineCount) invalid()
  return { snapshotId: v.snapshotId, orderBatchId: v.orderBatchId, submittedRequestId: v.submittedRequestId, tableSessionId: session, tableId: v.tableId, productionStationId: v.productionStationId, submittedAt: v.submittedAt, lineCount: v.lineCount, orderLineIds: [...v.orderLineIds] }
}
export function parseCashHistoryProductionPage(v: unknown, branch: string, request: string, query: CashHistoryProductionQuery, session?: string): CashHistoryProductionPage {
  validateCashHistoryProductionQuery(query)
  const s = subject(v, branch, request, session)
  if (!object(v) || !Array.isArray(v.items) || v.items.length > (query.limit ?? 20) || !(v.nextCursor === null || cursor(v.nextCursor)) || v.nextCursor !== null && v.items.length === 0) invalid()
  const items = v.items.map(raw => item(raw, s.tableSessionId))
  if (new Set(items.map(i => i.snapshotId)).size !== items.length || items.some(i => query.orderBatchId !== undefined && i.orderBatchId !== query.orderBatchId || query.productionStationId !== undefined && i.productionStationId !== query.productionStationId || query.orderLineId !== undefined && !i.orderLineIds.includes(query.orderLineId))) invalid()
  return { ...s, items, nextCursor: v.nextCursor }
}
export function parseCashHistoryProductionDetail(v: unknown, branch: string, request: string, snapshotId: string, session?: string): CashHistoryProductionDetail {
  const s = subject(v, branch, request, session)
  if (!object(v) || !Array.isArray(v.lines)) invalid()
  const snapshot = item(v.snapshot, s.tableSessionId)
  if (snapshot.snapshotId !== snapshotId || v.lines.length !== snapshot.lineCount) invalid()
  const lines = v.lines.map(raw => {
    if (!object(raw) || !isCashHistoryId(raw.orderLineId) || !positive(raw.lineSequence) || !text(raw.displayName, 512) || raw.displayName.trim().length === 0 || !positive(raw.quantity) || !['DineIn', 'Takeaway'].includes(raw.fulfillmentType as string) || !(raw.comment === null || text(raw.comment, 4096)) || !Array.isArray(raw.options)) invalid()
    const options = raw.options.map(o => {
      if (!object(o) || !(o.optionGroupId === null || isCashHistoryId(o.optionGroupId)) || !text(o.displayName, 512) || o.displayName.trim().length === 0 || !positive(o.quantityPerItem)) invalid()
      return { optionGroupId: o.optionGroupId, displayName: o.displayName, quantityPerItem: o.quantityPerItem }
    })
    return { orderLineId: raw.orderLineId, lineSequence: raw.lineSequence, displayName: raw.displayName, quantity: raw.quantity, fulfillmentType: raw.fulfillmentType as 'DineIn' | 'Takeaway', comment: raw.comment as string | null, options }
  })
  if (lines.some((line, index) => line.orderLineId !== snapshot.orderLineIds[index] || index > 0 && line.lineSequence <= lines[index - 1].lineSequence)) invalid()
  return { ...s, snapshot, lines }
}
