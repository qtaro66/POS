import { isCashHistoryId } from './cashHistoryContracts.ts'
import { isProductionSnapshotId } from './cashHistoryProductionContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface KitchenReprintTarget {
  cashRequestId: string; snapshotId: string; orderLineId: string; originalPrintJobId: string
  tableSessionId: string; orderBatchId: string; productionStationId: string
}
export interface KitchenReprintReceipt {
  schemaVersion: 1; branchId: string; requestId: string; cashRequestId: string
  tableSessionId: string; orderBatchId: string; orderLineId: string; originalSnapshotId: string
  originalPrintJobId: string; reprintSnapshotId: string; reprintPrintJobId: string; productionStationId: string; queuedAt: string
}
export interface KitchenReprintStatus {
  schemaVersion: 1; branchId: string; asOf: string; receipt: KitchenReprintReceipt
  status: 'PrintQueued' | 'Dispatching' | 'PrintDispatched' | 'Failed' | 'Unknown' | 'Completed'; attemptCount: number
}
export type KitchenReprintResult =
  | { kind: 'queued'; receipt: KitchenReprintReceipt }
  | { kind: 'rejected'; httpStatus: number }
  | { kind: 'uncertain'; reason: 'network' | 'timeout' | 'aborted' | 'protocol' | 'http'; httpStatus?: number }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
export const isKitchenReprintCommandId = (v: unknown): v is string => typeof v === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/.test(v)
function invalid(): never { throw new Error('Invalid kitchen reprint evidence.') }
export function validateKitchenReprintTarget(value: unknown): KitchenReprintTarget {
  if (!object(value) || !['cashRequestId', 'orderLineId', 'tableSessionId', 'orderBatchId', 'productionStationId'].every(key => isCashHistoryId(value[key])) ||
    !isProductionSnapshotId(value.snapshotId) || !isProductionSnapshotId(value.originalPrintJobId) || Object.keys(value).length !== 7) invalid()
  return Object.freeze({ cashRequestId: value.cashRequestId as string, snapshotId: value.snapshotId, orderLineId: value.orderLineId as string,
    originalPrintJobId: value.originalPrintJobId, tableSessionId: value.tableSessionId as string, orderBatchId: value.orderBatchId as string, productionStationId: value.productionStationId as string })
}
export const isKitchenReprintReason = (value: unknown): value is string => typeof value === 'string' && value.trim().length > 0 && value.length <= 4096 &&
  !Array.from(value).some(c => { const n = c.charCodeAt(0); return (n < 32 || n >= 127 && n <= 159) && !['\r', '\n', '\t'].includes(c) })
export function parseKitchenReprintReceipt(value: unknown, branchId: string, commandRequestId: string, expected?: KitchenReprintTarget): KitchenReprintReceipt {
  if (!isCashHistoryId(branchId) || !isKitchenReprintCommandId(commandRequestId) || !object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || value.requestId !== commandRequestId ||
    !['cashRequestId', 'tableSessionId', 'orderBatchId', 'orderLineId', 'productionStationId'].every(key => isCashHistoryId(value[key])) ||
    !isProductionSnapshotId(value.originalSnapshotId) || !isProductionSnapshotId(value.originalPrintJobId) || !isUtcTimestamp(value.queuedAt) ||
    value.reprintSnapshotId !== `${branchId}:${commandRequestId}:kitchen-reprint-snapshot:1` || value.reprintPrintJobId !== `${branchId}:${commandRequestId}:kitchen-reprint-print:1` ||
    value.originalSnapshotId === value.reprintSnapshotId || value.originalPrintJobId === value.reprintPrintJobId) invalid()
  if (expected) {
    const target = validateKitchenReprintTarget(expected)
    if (value.cashRequestId !== target.cashRequestId || value.tableSessionId !== target.tableSessionId || value.orderBatchId !== target.orderBatchId ||
      value.orderLineId !== target.orderLineId || value.originalSnapshotId !== target.snapshotId || value.originalPrintJobId !== target.originalPrintJobId ||
      value.productionStationId !== target.productionStationId) invalid()
  }
  return { schemaVersion: 1, branchId, requestId: commandRequestId, cashRequestId: value.cashRequestId as string, tableSessionId: value.tableSessionId as string,
    orderBatchId: value.orderBatchId as string, orderLineId: value.orderLineId as string, originalSnapshotId: value.originalSnapshotId,
    originalPrintJobId: value.originalPrintJobId, reprintSnapshotId: value.reprintSnapshotId, reprintPrintJobId: value.reprintPrintJobId,
    productionStationId: value.productionStationId as string, queuedAt: value.queuedAt }
}
export function parseKitchenReprintStatus(value: unknown, branchId: string, commandRequestId: string, expected?: KitchenReprintTarget): KitchenReprintStatus {
  if (!object(value) || value.schemaVersion !== 1 || value.branchId !== branchId || !isUtcTimestamp(value.asOf) ||
    !['PrintQueued', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown', 'Completed'].includes(value.status as string) ||
    !Number.isInteger(value.attemptCount) || (value.attemptCount as number) < 0 || (value.attemptCount as number) > 2147483647) invalid()
  return { schemaVersion: 1, branchId, asOf: value.asOf, receipt: parseKitchenReprintReceipt(value.receipt, branchId, commandRequestId, expected),
    status: value.status as KitchenReprintStatus['status'], attemptCount: value.attemptCount as number }
}
