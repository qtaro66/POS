import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface DeliveryChannel {
  totalCount: number; pendingCount: number; deliveredCount: number; inFlightCount: number
  delayedCount: number; expiredLeaseCount: number; unknownCount: number
  lastConfirmedAt: string | null; oldestPendingAt: string | null
}
export interface DeliveryOverview {
  schemaVersion: 1
  snapshot: { branchId: string; asOf: string; state: 'NoHistory' | 'Pending' | 'Current'; lastConfirmedAt: string | null
    tableEvents: DeliveryChannel; closingReports: DeliveryChannel; replayPendingCount: number; replayCompletedCount: number
    replayLastConfirmedAt: string | null; replayOldestPendingAt: string | null }
  tableDeliveryEnabled: boolean; reportDeliveryEnabled: boolean
}
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const count = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 0
const nullableTime = (value: unknown): value is string | null => value === null || isUtcTimestamp(value)
function invalid(): never { throw new Error('Invalid delivery overview.') }
export function deliveryTimeOrder(value: string): string {
  const parts = /^(.*T\d{2}:\d{2}:\d{2})(?:\.(\d{1,7}))?(?:Z|\+00:00)$/.exec(value)!
  return parts[1] + '.' + (parts[2] ?? '').padEnd(7, '0')
}
function channel(value: unknown): DeliveryChannel {
  if (!object(value) || ![value.totalCount, value.pendingCount, value.deliveredCount, value.inFlightCount,
    value.delayedCount, value.expiredLeaseCount, value.unknownCount].every(count) ||
    !nullableTime(value.lastConfirmedAt) || !nullableTime(value.oldestPendingAt)) invalid()
  const total = value.totalCount as number, pending = value.pendingCount as number, delivered = value.deliveredCount as number
  const active = value.inFlightCount as number, delayed = value.delayedCount as number, expired = value.expiredLeaseCount as number
  if (!Number.isSafeInteger(pending + delivered) || total !== pending + delivered ||
    !Number.isSafeInteger(active + delayed + expired) || active + delayed + expired > pending ||
    (value.unknownCount as number) > pending || ((delivered === 0) !== (value.lastConfirmedAt === null)) ||
    ((pending === 0) !== (value.oldestPendingAt === null))) invalid()
  return { totalCount: total, pendingCount: pending, deliveredCount: delivered, inFlightCount: active,
    delayedCount: delayed, expiredLeaseCount: expired, unknownCount: value.unknownCount as number,
    lastConfirmedAt: value.lastConfirmedAt, oldestPendingAt: value.oldestPendingAt }
}
export function parseDeliveryOverview(value: unknown, branchId: string): DeliveryOverview {
  if (!object(value) || value.schemaVersion !== 1 || typeof value.tableDeliveryEnabled !== 'boolean' ||
    typeof value.reportDeliveryEnabled !== 'boolean' || !object(value.snapshot)) invalid()
  const saved = value.snapshot
  if (saved.branchId !== branchId || !isUtcTimestamp(saved.asOf) || !count(saved.replayPendingCount) ||
    !count(saved.replayCompletedCount) || !nullableTime(saved.replayLastConfirmedAt) || !nullableTime(saved.replayOldestPendingAt) ||
    !nullableTime(saved.lastConfirmedAt) || ((saved.replayCompletedCount === 0) !== (saved.replayLastConfirmedAt === null)) ||
    ((saved.replayPendingCount === 0) !== (saved.replayOldestPendingAt === null))) invalid()
  const tableEvents = channel(saved.tableEvents), closingReports = channel(saved.closingReports)
  const pending = tableEvents.pendingCount > 0 || closingReports.pendingCount > 0 || saved.replayPendingCount > 0
  const history = tableEvents.deliveredCount > 0 || closingReports.deliveredCount > 0 || saved.replayCompletedCount > 0
  const state = pending ? 'Pending' : history ? 'Current' : 'NoHistory'
  const timestamps = [tableEvents.lastConfirmedAt, closingReports.lastConfirmedAt, saved.replayLastConfirmedAt]
    .filter((stamp): stamp is string => stamp !== null)
  const latest = timestamps.reduce<string | null>((previous, stamp) => previous === null || deliveryTimeOrder(stamp) > deliveryTimeOrder(previous) ? stamp : previous, null)
  if (saved.state !== state || ((latest === null) !== (saved.lastConfirmedAt === null)) ||
    (latest !== null && deliveryTimeOrder(latest) !== deliveryTimeOrder(saved.lastConfirmedAt as string))) invalid()
  return { schemaVersion: 1, snapshot: { branchId, asOf: saved.asOf, state, lastConfirmedAt: saved.lastConfirmedAt,
    tableEvents, closingReports, replayPendingCount: saved.replayPendingCount, replayCompletedCount: saved.replayCompletedCount,
    replayLastConfirmedAt: saved.replayLastConfirmedAt, replayOldestPendingAt: saved.replayOldestPendingAt },
    tableDeliveryEnabled: value.tableDeliveryEnabled, reportDeliveryEnabled: value.reportDeliveryEnabled }
}
