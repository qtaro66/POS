import type { DeliveryChannel, DeliveryOverview } from './deliveryOverviewContracts.ts'
export const branch = 'BRANCH-01'
export const at = '2026-11-21T03:00:00.1234567Z'
export function deliveryChannel(pending = 0, delivered = 0): DeliveryChannel {
  return { totalCount: pending + delivered, pendingCount: pending, deliveredCount: delivered,
    inFlightCount: 0, delayedCount: 0, expiredLeaseCount: 0, unknownCount: 0,
    lastConfirmedAt: delivered ? at : null, oldestPendingAt: pending ? '2026-11-20T03:00:00Z' : null }
}
export function deliveryView(tables = 0, reports = 0, replay = 0, delivered = 0, lifecycle = 0): DeliveryOverview {
  return { schemaVersion: 2, snapshot: { branchId: branch, asOf: at,
    state: tables || reports || replay || lifecycle ? 'Pending' : delivered ? 'Current' : 'NoHistory',
    lastConfirmedAt: delivered ? at : null, tableEvents: deliveryChannel(tables, delivered), closingReports: deliveryChannel(reports), lifecycleReports: deliveryChannel(lifecycle),
    replayPendingCount: replay, replayCompletedCount: 0, replayLastConfirmedAt: null,
    replayOldestPendingAt: replay ? '2026-11-19T03:00:00Z' : null }, tableDeliveryEnabled: true, reportDeliveryEnabled: true, lifecycleDeliveryEnabled: true }
}
