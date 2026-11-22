import { describe, expect, it } from 'vitest'
import { parseClosingReportDetail, type ClosingLifecycleDelivery } from './closingReportWorkspaceContracts.ts'
import { frozenLifecycleDetail } from './frozenLifecycleTestFixtures.ts'
import { branch, now, request } from './closingReportTestFixtures.ts'
import { deliveryChannel, deliveryView } from './deliveryOverviewTestFixtures.ts'
import { parseDeliveryOverview } from './deliveryOverviewContracts.ts'

export function contextDetail(state: ClosingLifecycleDelivery['deliveryState'] = 'Queued') {
  const detail = frozenLifecycleDetail()
  detail.report.deliveryState = 'Delivered'; detail.report.deliveredAt = now; detail.report.attemptCount = 1
  const leased = state === 'InFlight' || state === 'LeaseExpired'
  const delivery: ClosingLifecycleDelivery = { contextId: 'lifecycle:' + detail.report.reportId, deliveryState: state,
    attemptCount: state === 'Queued' ? 0 : 1, availableAt: now, leaseExpiresAt: leased ? now : null,
    deliveredAt: state === 'Delivered' ? now : null, lastFailure: state === 'RetryScheduled' ? 'ParentUnavailable' : null }
  return { ...detail, lifecycleDelivery: delivery }
}
describe('lifecycle delivery boundary', () => {
  it.each(['Queued', 'InFlight', 'RetryScheduled', 'LeaseExpired', 'Delivered'] as const)('accepts independently scoped %s and projects public fields only', state => {
    const value = contextDetail(state)
    const result = parseClosingReportDetail({ ...value, lifecycleDelivery: { ...value.lifecycleDelivery, leaseOwner: 'PRIVATE' } }, branch, request)
    expect(result.lifecycleDelivery?.deliveryState).toBe(state)
    expect(JSON.stringify(result)).not.toContain('PRIVATE')
  })
  it('keeps legacy absent/null context receipt unknown rather than accepted', () => {
    const value = frozenLifecycleDetail()
    expect(parseClosingReportDetail(value, branch, request).lifecycleDelivery).toBeNull()
    expect(parseClosingReportDetail({ ...value, lifecycleDelivery: null }, branch, request).lifecycleDelivery).toBeNull()
  })
  it.each([
    ['foreign context', (value: ReturnType<typeof contextDetail>) => { value.lifecycleDelivery.contextId = 'lifecycle:foreign' }],
    ['fake delivery', value => { value.lifecycleDelivery.deliveredAt = now }],
    ['unknown failure', value => { value.lifecycleDelivery.lastFailure = 'PRIVATE' as 'ParentUnavailable' }],
    ['unsafe attempts', value => { value.lifecycleDelivery.attemptCount = Number.MAX_SAFE_INTEGER + 1 }],
    ['bad UTC', value => { value.lifecycleDelivery.availableAt = '2026-11-21T10:00:00+07:00' }],
    ['missing lease', value => { value.lifecycleDelivery.deliveryState = 'InFlight'; value.lifecycleDelivery.attemptCount = 1 }],
    ['unconfirmed parent', value => { value.lifecycleDelivery.deliveryState = 'Delivered'; value.lifecycleDelivery.deliveredAt = now; value.lifecycleDelivery.attemptCount = 1; value.report.deliveryState = 'Queued'; value.report.deliveredAt = null; value.report.attemptCount = 0 }],
  ] as [string, (value: ReturnType<typeof contextDetail>) => void][])('rejects inconsistent %s', (_, mutate) => {
    const value = contextDetail(); mutate(value)
    expect(() => parseClosingReportDetail(value, branch, request)).toThrow()
  })
  it('does not accept a delivery record without its immutable local context', () => {
    expect(() => parseClosingReportDetail({ ...contextDetail(), lifecycle: null }, branch, request)).toThrow()
  })
  it('allows sanitized missing failure details on delayed recovery', () => {
    const value = contextDetail('RetryScheduled'); value.lifecycleDelivery.lastFailure = null
    expect(parseClosingReportDetail(value, branch, request).lifecycleDelivery?.lastFailure).toBeNull()
  })
})
describe('four-source delivery overview v2', () => {
  it('keeps all sixteen pending combinations honest despite recent receipt history', () => {
    for (const tables of [0, 1]) for (const reports of [0, 1]) for (const replay of [0, 1]) for (const lifecycle of [0, 1]) {
      const value = deliveryView(tables, reports, replay, 1, lifecycle)
      expect(parseDeliveryOverview(value, value.snapshot.branchId).snapshot.state).toBe(tables || reports || replay || lifecycle ? 'Pending' : 'Current')
    }
  })
  it('includes exact context receipt in latest confirmation and rejects missing or legacy sources', () => {
    const value = deliveryView(0, 0, 0, 1)
    value.snapshot.lifecycleReports = deliveryChannel(0, 1)
    value.snapshot.lifecycleReports.lastConfirmedAt = '2026-11-21T03:00:00.1234568Z'
    value.snapshot.lastConfirmedAt = value.snapshot.lifecycleReports.lastConfirmedAt
    expect(parseDeliveryOverview(value, value.snapshot.branchId).snapshot.lastConfirmedAt).toBe('2026-11-21T03:00:00.1234568Z')
    expect(() => parseDeliveryOverview({ ...value, schemaVersion: 1 }, value.snapshot.branchId)).toThrow()
    expect(() => parseDeliveryOverview({ ...value, snapshot: { ...value.snapshot, lifecycleReports: null } }, value.snapshot.branchId)).toThrow()
    expect(() => parseDeliveryOverview({ ...value, lifecycleDeliveryEnabled: undefined }, value.snapshot.branchId)).toThrow()
    value.snapshot.lastConfirmedAt = '2026-11-21T03:00:00.1234567Z'
    expect(() => parseDeliveryOverview(value, value.snapshot.branchId)).toThrow()
  })
})
