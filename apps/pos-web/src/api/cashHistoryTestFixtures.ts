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
export function cashItemizedDetail(): CashHistoryDetail {
  return { ...cashDetail(cashItem({ tableId: 'A01', orderBatchIds: ['ORDER-1'] })), bill: {
    schemaVersion: 1, branchId: 'branch', tableSessionId: 'SESSION-1', tableId: 'A01', paymentRequestId: 'PREPARE-1',
    capturedAt: '2026-11-24T02:58:00Z', masterDataVersion: 'menu-original', calculationPolicyVersion: 'policy-original', vatRate: 7, serviceChargeRate: 5,
    items: [{ orderLineId: 'LINE-1', orderBatchId: 'ORDER-1', sellableItemId: 'ITEM-1', displayName: 'ข้าวเดิม\nชื่อที่บันทึก', quantity: 2, unitPrice: 50,
      fulfillmentType: 'Takeaway', comment: 'ไม่เผ็ด\nแยกน้ำ', options: [{ optionId: 'OPTION-1', displayName: 'ไข่เดิม', quantityPerItem: 1, unitPrice: 10 }],
      grossAmount: 120, discount: { kind: 'FixedAmount', requestedValue: 10, requestedAmount: 10, appliedAmount: 10 }, netAmount: 110 }],
    billDiscount: { kind: 'FixedAmount', requestedValue: 5, requestedAmount: 5, appliedAmount: 5 },
  } }
}
