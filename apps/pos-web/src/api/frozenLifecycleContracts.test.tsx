import { describe, expect, it } from 'vitest'
import { frozenLifecycleDetail } from './frozenLifecycleTestFixtures.ts'
import { branch, reportDetail, request } from './closingReportTestFixtures.ts'
import { parseClosingReportDetail } from './closingReportWorkspaceContracts.ts'

describe('frozen local report lifecycle context', () => {
  it('projects the original multi-shift facts without adding informational shift floats', () => {
    const saved = frozenLifecycleDetail()
    const result = parseClosingReportDetail(saved, branch, request)
    expect(result.lifecycle?.openingCashAmount).toBe(500)
    expect(result.lifecycle?.shifts.map(value => value.openingCashAmount)).toEqual([500, 200, null])
    expect(result.lifecycle?.shifts.map(value => value.closing.actualCashAmount)).toEqual([null, 0, 490])
    expect(result.snapshot.closing.expectedCashAmount).toBe(500)
    expect(result.lifecycle?.shifts.at(-1)?.closing).toEqual(result.snapshot.closing)
    ;(saved.lifecycle as unknown as Record<string, unknown>).privateToken = 'PRIVATE'
    expect(JSON.stringify(parseClosingReportDetail(saved, branch, request))).not.toContain('PRIVATE')
  })
  it('keeps missing and explicitly unavailable legacy context unknown', () => {
    const old = reportDetail()
    expect(parseClosingReportDetail(old, branch, request).lifecycle).toBeNull()
    old.lifecycle = null
    expect(parseClosingReportDetail(old, branch, request).lifecycle).toBeNull()
  })
  it.each([
    ['schema', (value: ReturnType<typeof frozenLifecycleDetail>) => { value.lifecycle.schemaVersion = 2 as 1 }],
    ['branch', value => { value.lifecycle.branchId = 'OTHER' }], ['request', value => { value.lifecycle.requestId = value.report.storeSessionId }],
    ['store', value => { value.lifecycle.storeSessionId = request }], ['terminal shift', value => { value.lifecycle.closingShiftId = request }],
    ['day', value => { value.lifecycle.businessDate = '2026-11-22' }], ['operation', value => { value.lifecycle.closesStore = false }],
    ['negative opening', value => { value.lifecycle.openingCashAmount = -1 }], ['unsafe opening', value => { value.lifecycle.openingCashAmount = 9007199254740992 }],
    ['duplicate shift', value => { value.lifecycle.shifts[1] = structuredClone(value.lifecycle.shifts[0]) }],
    ['first float', value => { value.lifecycle.shifts[0].openingCashAmount = 0 }], ['first opener', value => { value.lifecycle.opened.employeeId = 'OTHER' }],
    ['opening business date', value => { value.lifecycle.opened.branchLocalDate = '2026-11-22'; value.lifecycle.shifts[0].opened.branchLocalDate = '2026-11-22' }],
    ['shift opening business date', value => { value.lifecycle.shifts[1].opened.branchLocalDate = '2026-11-22' }],
    ['shift closing business date', value => { value.lifecycle.shifts[0].closing.context.branchLocalDate = '2026-11-22' }],
    ['empty shifts', value => { value.lifecycle.shifts = [] }], ['early store close', value => { value.lifecycle.shifts[0].closing.closesStore = true }],
    ['terminal difference', value => { Object.assign(value.lifecycle.shifts.at(-1)!.closing, { actualCashAmount: 480, cashDifference: -20 }) }],
    ['terminal attribution', value => { value.lifecycle.shifts.at(-1)!.closing.context.employeeId = 'OTHER' }],
    ['wrong store closing', value => { value.lifecycle.shifts[0].closing.storeSessionId = request }],
    ['sub-cent float', value => { value.lifecycle.shifts[1].openingCashAmount = 0.001 }],
    ['uncounted difference', value => { value.lifecycle.shifts[0].closing.cashDifference = 0 }],
    ['reconciliation', value => { value.lifecycle.shifts[1].closing.reconciliationStatus = 'NotReconciled' }],
    ['offset', value => { value.lifecycle.shifts[1].opened.occurredAt = '2026-11-21T08:00:00+07:00' }],
    ['invalid date', value => { value.lifecycle.shifts[1].opened.branchLocalDate = '2026-02-30' }],
  ] as [string, (value: ReturnType<typeof frozenLifecycleDetail>) => void][])('rejects inconsistent %s', (_, change) => {
    const value = frozenLifecycleDetail(); change(value)
    expect(() => parseClosingReportDetail(value, branch, request)).toThrow()
  })
  it('uses recorded sequence rather than rejecting legitimate wall-clock corrections', () => {
    const value = frozenLifecycleDetail()
    value.lifecycle.shifts[1].opened.occurredAt = '2026-11-20T23:59:00Z'
    expect(parseClosingReportDetail(value, branch, request).lifecycle?.shifts[1].opened.occurredAt).toBe('2026-11-20T23:59:00Z')
  })
  it('accepts an uncounted terminal CloseShift with an informational nullable opening', () => {
    const value = frozenLifecycleDetail()
    value.report.operation = 'CloseShift'; value.snapshot.operation = 'CloseShift'; value.lifecycle.closesStore = false
    Object.assign(value.snapshot.closing, { closesStore: false, actualCashAmount: null, cashDifference: null, reconciliationStatus: 'NotReconciled' })
    value.lifecycle.shifts.at(-1)!.closing = structuredClone(value.snapshot.closing)
    expect(parseClosingReportDetail(value, branch, request).lifecycle?.shifts.at(-1)?.closing.actualCashAmount).toBeNull()
  })
})
