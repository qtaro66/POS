import type { CashHistoryDetail, CashHistoryItem, CashHistoryPage } from './cashHistoryContracts.ts'
export const historyTime = '2026-11-24T03:00:00Z'
export function cashItem(change: Partial<CashHistoryItem> = {}): CashHistoryItem {
  return { requestId: 'CASH-1', tableSessionId: 'SESSION-1', receiptNumber: 'TEST-001', businessDate: '2026-11-24', storeSessionId: 'STORE-1', shiftId: 'SHIFT-1',
    employeeId: 'employee-1', deviceId: 'device-1', capturedAt: historyTime, paymentMethod: 'Cash', status: 'Completed', tenderedAmount: 120, appliedAmount: 110, changeAmount: 10,
    capturedPrintStatus: 'QueuedForSimulation', ...change }
}
export const cashPage = (items: CashHistoryItem[] = [cashItem()], nextCursor: string | null = null): CashHistoryPage => ({ schemaVersion: 1, branchId: 'branch', asOf: historyTime, businessDate: '2026-11-24', items, nextCursor })
export const cashDetail = (transaction = cashItem()): CashHistoryDetail => ({ schemaVersion: 1, branchId: 'branch', asOf: historyTime, transaction,
  financials: { itemGrossAmount: 120, lineDiscountAmount: 10, billDiscountAmount: 5, serviceChargeAmount: 5.9, totalBeforeRounding: 110.9, roundingAdjustment: -0.9, amountDue: 110, vatInclusiveAmount: 7.26, amountBeforeVat: 103.64 } })
