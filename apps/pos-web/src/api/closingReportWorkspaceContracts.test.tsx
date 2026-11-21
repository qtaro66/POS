import { describe, expect, it } from 'vitest'
import { createOrderingClient, type OrderingSession } from './orderingClient.ts'
import { parseClosingReportDelivery, parseClosingReportDetail, parseClosingReportPage, validateClosingReportQuery } from './closingReportWorkspaceContracts.ts'
import { branch, now, reportDetail, reportItem, reportPage, request, shift, store } from './closingReportTestFixtures.ts'

describe('saved closing report contracts', () => {
  it('binds pages and details to the authenticated branch and requested identities and filters', () => {
    expect(parseClosingReportPage(reportPage(), branch, { status: 'Pending', storeSessionId: store, shiftId: shift, businessDate: '2026-11-21' }).items).toHaveLength(1)
    expect(parseClosingReportDetail(reportDetail(), branch, request).snapshot.cashSummary?.tables?.openTableCount).toBe(2)
    expect(() => parseClosingReportPage(reportPage(), 'OTHER', {})).toThrow()
    expect(() => parseClosingReportPage(reportPage(), branch, { status: 'Delivered' })).toThrow()
    expect(() => parseClosingReportDetail(reportDetail(), branch, store)).toThrow()
    const detail = reportDetail(); detail.snapshot.shiftId = request
    expect(() => parseClosingReportDetail(detail, branch, request)).toThrow()
  })
  it.each([
    { status: 'bad' }, { storeSessionId: 'bad' }, { shiftId: 'bad' }, { businessDate: '2026-02-30' }, { limit: 0 }, { limit: 51 }, { cursor: '../unsafe' }
  ])('rejects invalid query %j', query => expect(() => validateClosingReportQuery(query as never)).toThrow())
  it('rejects duplicate rows, mismatched report ID, invalid UTC, unsafe counters and inconsistent delivery state', () => {
    expect(() => parseClosingReportPage(reportPage([reportItem(), reportItem()]), branch, {})).toThrow()
    for (const change of [{ reportId: 'other' }, { attemptCount: -1 }, { createdAt: '2026-11-21T10:00:00+07:00' },
      { deliveryState: 'Delivered', deliveredAt: null }, { deliveryState: 'InFlight', leaseExpiresAt: null },
      { deliveryState: 'LeaseExpired', attemptCount: 1, leaseExpiresAt: '2026-11-21T03:01:00Z' }]) {
      expect(() => parseClosingReportPage(reportPage([reportItem(change as never)]), branch, {})).toThrow()
    }
  })
  it('accepts retry scheduled with an unavailable prior failure and projects public delivery config', () => {
    const item = reportItem({ deliveryState: 'RetryScheduled', attemptCount: 1, availableAt: '2026-11-21T03:01:00Z' })
    expect(parseClosingReportPage(reportPage([item]), branch, {}).items[0].lastFailure).toBeNull()
    expect(parseClosingReportDelivery({ branchId: branch, asOf: now, deliveryEnabled: false, centralOrigin: 'secret' }, branch))
      .toEqual({ branchId: branch, asOf: now, deliveryEnabled: false })
    expect(() => parseClosingReportDelivery({ branchId: 'OTHER', asOf: now, deliveryEnabled: true }, branch)).toThrow()
  })
  it('preserves unknown legacy summaries and uncounted shifts without replacing them by zero', () => {
    const detail = reportDetail(reportItem({ operation: 'CloseShift' })); detail.snapshot.cashSummary = null
    Object.assign(detail.snapshot.closing, { actualCashAmount: null, cashDifference: null, reconciliationStatus: 'NotReconciled' })
    expect(parseClosingReportDetail(detail, branch, request).snapshot.cashSummary).toBeNull()
    expect(parseClosingReportDetail(detail, branch, request).snapshot.closing.actualCashAmount).toBeNull()
  })
  it('validates money using safe integer cents, including large valid decimals and unsafe summaries', () => {
    const detail = reportDetail()
    Object.assign(detail.snapshot.closing, { expectedCashAmount: 1234567890.12, actualCashAmount: 1234567880.12, cashDifference: -10 })
    expect(parseClosingReportDetail(detail, branch, request).snapshot.closing.expectedCashAmount).toBe(1234567890.12)
    detail.snapshot.cashSummary!.store.appliedCashAmount = 9007199254740992
    expect(() => parseClosingReportDetail(detail, branch, request)).toThrow()
    const subcent = reportDetail(); subcent.snapshot.cashSummary!.store.financials!.vatInclusiveAmount = 0.001
    expect(() => parseClosingReportDetail(subcent, branch, request)).toThrow()
    const fractional = reportDetail(); fractional.snapshot.closing.expectedCashAmount = Number('10000000000000.005')
    expect(() => parseClosingReportDetail(fractional, branch, request)).toThrow()
    const wrong = reportDetail(); wrong.snapshot.closing.cashDifference = -9.99
    expect(() => parseClosingReportDetail(wrong, branch, request)).toThrow()
  })
})

it('requests the authenticated list/detail/delivery routes, binds responses and refuses invalid outbound IDs', async () => {
  const urls: string[] = []
  const session: OrderingSession = { serviceOrigin: 'https://branch.example', branchId: branch, deviceId: 'device', employeeId: 'employee', deviceCredential: 'credential', employeeSessionToken: 'token' }
  const client = createOrderingClient(session, { fetch: (async (url: string | URL | Request) => {
    urls.push(String(url))
    return new Response(JSON.stringify(String(url).endsWith('/delivery') ? { branchId: branch, asOf: now, deliveryEnabled: false } : String(url).endsWith(request) ? reportDetail() : reportPage()), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }) as typeof fetch })
  expect((await client.closingReports({ status: 'Pending', storeSessionId: store, shiftId: shift, businessDate: '2026-11-21', cursor: 'opaque' })).kind).toBe('ok')
  expect((await client.closingReportDetail(request)).kind).toBe('ok')
  expect((await client.closingReportDelivery()).kind).toBe('ok')
  expect(urls[0]).toContain('status=Pending')
  expect(urls[0]).toContain('cursor=opaque')
  const calls = urls.length
  expect((await client.closingReportDetail('../wrong')).kind).toBe('unavailable')
  expect((await client.closingReports({ limit: 51 })).kind).toBe('unavailable')
  expect(urls).toHaveLength(calls)
})

it('compares UTC delivery times at original seven-digit precision', () => {
  const asOf = '2026-11-21T03:00:00.1234560Z'
  const item = reportItem({ deliveryState: 'InFlight', attemptCount: 1, leaseExpiresAt: '2026-11-21T03:00:00.1234567+00:00' })
  expect(parseClosingReportPage({ ...reportPage([item]), asOf }, branch, {}).items[0].deliveryState).toBe('InFlight')
  const retry = reportItem({ deliveryState: 'RetryScheduled', attemptCount: 1, availableAt: '2026-11-21T03:00:00.1234567Z' })
  expect(parseClosingReportPage({ ...reportPage([retry]), asOf }, branch, {}).items[0].deliveryState).toBe('RetryScheduled')
})
