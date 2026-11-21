import type { ClosingReportLifecycle, ClosingReportDetail } from './closingReportWorkspaceContracts.ts'
import { reportDetail, shift } from './closingReportTestFixtures.ts'
export const firstShift = '00000000-0000-0000-0000-000000000011'
export const secondShift = '00000000-0000-0000-0000-000000000012'
export function frozenLifecycleDetail(): ClosingReportDetail & { lifecycle: ClosingReportLifecycle } {
  const detail = reportDetail()
  const opened = { employeeId: 'store-opener', deviceId: 'opening-device', occurredAt: '2026-11-21T00:00:00Z', branchLocalDate: detail.snapshot.businessDate }
  const context = (id: string, time: string) => ({ employeeId: id, deviceId: id + '-device', occurredAt: time, branchLocalDate: detail.snapshot.businessDate })
  return { ...detail, lifecycle: { schemaVersion: 1, branchId: detail.branchId, requestId: detail.report.requestId, storeSessionId: detail.report.storeSessionId,
    closingShiftId: shift, businessDate: detail.snapshot.businessDate, openingCashAmount: 500, opened, closesStore: true,
    shifts: [{ shiftId: firstShift, openingCashAmount: 500, opened: { ...opened }, closing: { ...detail.snapshot.closing, shiftId: firstShift, closesStore: false,
      actualCashAmount: null, cashDifference: null, reconciliationStatus: 'NotReconciled', context: context('first-closer', '2026-11-21T01:00:00Z') } },
    { shiftId: secondShift, openingCashAmount: 200, opened: context('second-opener', '2026-11-21T01:01:00Z'), closing: { ...detail.snapshot.closing,
      shiftId: secondShift, closesStore: false, actualCashAmount: 0, cashDifference: -500, reconciliationStatus: 'Reconciled', context: context('second-closer', '2026-11-21T02:00:00Z') } },
    { shiftId: shift, openingCashAmount: null, opened: context('final-opener', '2026-11-21T02:01:00Z'), closing: structuredClone(detail.snapshot.closing) }] } }
}
