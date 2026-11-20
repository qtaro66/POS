import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface ClosingReportQueueStatus {
  branchId: string
  pendingCount: number
  leasedCount: number
  deliveredCount: number
  lastDeliveredAt: string | null
}

export function parseClosingReportQueueStatus(value: unknown, branchId: string): ClosingReportQueueStatus {
  if (value === null || typeof value !== 'object' || Array.isArray(value)) throw new Error('Invalid closing queue status.')
  const body = value as Record<string, unknown>
  if (body.branchId !== branchId || ![body.pendingCount, body.leasedCount, body.deliveredCount]
    .every(count => Number.isSafeInteger(count) && (count as number) >= 0) ||
    (body.leasedCount as number) > (body.pendingCount as number) ||
    !(body.lastDeliveredAt === null || isUtcTimestamp(body.lastDeliveredAt)) ||
    ((body.deliveredCount === 0) !== (body.lastDeliveredAt === null))) throw new Error('Invalid closing queue status.')
  return { branchId, pendingCount: body.pendingCount as number, leasedCount: body.leasedCount as number,
    deliveredCount: body.deliveredCount as number, lastDeliveredAt: body.lastDeliveredAt as string | null }
}
