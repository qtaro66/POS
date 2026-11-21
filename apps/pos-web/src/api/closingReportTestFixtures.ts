import type { ClosingReportDetail, ClosingReportItem, ClosingReportPage } from './closingReportWorkspaceContracts.ts'
export const branch = 'BRANCH'
export const now = '2026-11-21T03:00:00Z'
export const request = '00000000-0000-0000-0000-000000000001'
export const store = '00000000-0000-0000-0000-000000000002'
export const shift = '00000000-0000-0000-0000-000000000003'
export const hex = (value: string) => Array.from(new TextEncoder().encode(value), byte => byte.toString(16).padStart(2, '0')).join('').toUpperCase()
export const reportItem = (changes: Partial<ClosingReportItem> = {}): ClosingReportItem => {
  const id = changes.requestId ?? request
  return { requestId: id, reportId: 'closing:' + hex(branch) + ':' + hex(id), storeSessionId: store, shiftId: shift,
    businessDate: '2026-11-21', operation: 'CloseStore', createdAt: now, deliveryState: 'Queued', attemptCount: 0,
    availableAt: now, leaseExpiresAt: null, deliveredAt: null, lastFailure: null, ...changes }
}
export const reportPage = (items = [reportItem()], nextCursor: string | null = null): ClosingReportPage => ({ branchId: branch, asOf: now, items, nextCursor })
export const reportDetail = (item = reportItem()): ClosingReportDetail => ({ branchId: branch, report: item, snapshot: {
  schemaVersion: 1, branchId: branch, reportId: item.reportId, requestId: item.requestId, storeSessionId: item.storeSessionId,
  shiftId: item.shiftId, operation: item.operation, businessDate: item.businessDate,
  closing: { storeSessionId: item.storeSessionId, shiftId: item.shiftId, closesStore: item.operation === 'CloseStore',
    expectedCashAmount: 500, actualCashAmount: 490, cashDifference: -10, reconciliationStatus: 'Reconciled',
    context: { employeeId: 'saved-employee', deviceId: 'saved-device', occurredAt: now, branchLocalDate: '2026-11-21' } },
  cashSummary: { source: 'CashSales', store: { receiptCount: 1, appliedCashAmount: 100, financials: { itemGrossAmount: 110,
    lineDiscountAmount: 5, billDiscountAmount: 5, serviceChargeAmount: 0, totalBeforeRounding: 100, roundingAdjustment: 0,
    amountDue: 100, vatInclusiveAmount: 6.54, amountBeforeVat: 93.46 } }, shift: { receiptCount: 1, appliedCashAmount: 100 },
    tables: { openTableCount: 2, awaitingPaymentTableCount: 1, partiallyPaidTableCount: 0 } }
} })
