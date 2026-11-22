import { expect, it } from 'vitest'
import { parseClosingReportDetail, parseClosingReportItem, parseClosingReportPage, type ClosingLifecycleDelivery, type ClosingReportItem, type ClosingReportState } from './closingReportWorkspaceContracts.ts'
import { branch, now, reportItem, reportPage, request } from './closingReportTestFixtures.ts'
import { frozenLifecycleDetail } from './frozenLifecycleTestFixtures.ts'

function contextRow(state: ClosingReportState = 'Queued'): ClosingReportItem & { lifecycleDelivery: ClosingLifecycleDelivery } {
  const item = reportItem({ deliveryState: 'Delivered', deliveredAt: now, attemptCount: 1 })
  return { ...item, lifecycleDelivery: { contextId: 'lifecycle:' + item.reportId, deliveryState: state,
    attemptCount: state === 'Queued' ? 0 : 1, availableAt: state === 'RetryScheduled' ? '2026-11-21T03:01:00Z' : now,
    leaseExpiresAt: state === 'InFlight' ? '2026-11-21T03:01:00Z' : state === 'LeaseExpired' ? now : null,
    deliveredAt: state === 'Delivered' ? now : null, lastFailure: state === 'RetryScheduled' ? 'ParentUnavailable' : null } }
}
it.each(['Queued', 'InFlight', 'LeaseExpired', 'RetryScheduled', 'Delivered'] as const)('projects independently bound %s companion metadata without private fields', state => {
  const item = contextRow(state)
  const value = { ...item, lifecycleDelivery: { ...item.lifecycleDelivery, leaseOwner: 'PRIVATE-WORKER', leaseId: 'PRIVATE-LEASE' } }
  const result = parseClosingReportPage(reportPage([value]), branch, {})
  expect(result.items[0]?.lifecycleDelivery).toEqual(item.lifecycleDelivery)
  expect(JSON.stringify(result)).not.toContain('PRIVATE')
  const filtered = state === 'Delivered' ? 'Delivered' : 'Pending'
  expect(parseClosingReportPage(reportPage([value]), branch, { status: 'Delivered', lifecycleStatus: filtered }).items).toHaveLength(1)
})
it('keeps absent older metadata unknown, explicit null historical, and filtered omissions unavailable', () => {
  const old = reportItem()
  expect(parseClosingReportItem(old, branch).lifecycleDelivery).toBeUndefined()
  expect(Object.hasOwn(parseClosingReportItem(old, branch), 'lifecycleDelivery')).toBe(false)
  const legacy = { ...old, lifecycleDelivery: null }
  expect(parseClosingReportPage(reportPage([legacy]), branch, { lifecycleStatus: 'NotRecorded' }).items[0]?.lifecycleDelivery).toBeNull()
  for (const lifecycleStatus of ['Pending', 'Delivered', 'NotRecorded'] as const) {
    expect(() => parseClosingReportPage(reportPage([old]), branch, { lifecycleStatus })).toThrow()
  }
  expect(parseClosingReportPage(reportPage([old]), branch, { lifecycleStatus: 'All' }).items).toHaveLength(1)
})
it('rejects wrong lifecycle membership even when original report receipt matches', () => {
  for (const [item, lifecycleStatus] of [[contextRow('Delivered'), 'Pending'], [contextRow(), 'Delivered'],
    [contextRow(), 'NotRecorded'], [{ ...reportItem(), lifecycleDelivery: null }, 'Pending']] as const) {
    expect(() => parseClosingReportPage(reportPage([item]), branch, { lifecycleStatus })).toThrow()
  }
})
it.each([
  ['foreign identity', (v: ReturnType<typeof contextRow>) => { v.lifecycleDelivery.contextId = 'lifecycle:foreign' }],
  ['fake receipt', v => { v.lifecycleDelivery.deliveredAt = now }],
  ['unknown failure', v => { v.lifecycleDelivery.lastFailure = 'PRIVATE' as 'ParentUnavailable' }],
  ['unsafe attempts', v => { v.lifecycleDelivery.attemptCount = -1 }],
  ['expired in flight', v => { v.lifecycleDelivery.deliveryState = 'InFlight'; v.lifecycleDelivery.attemptCount = 1; v.lifecycleDelivery.leaseExpiresAt = now }],
  ['future expired lease', v => { v.lifecycleDelivery.deliveryState = 'LeaseExpired'; v.lifecycleDelivery.attemptCount = 1; v.lifecycleDelivery.leaseExpiresAt = '2026-11-21T03:01:00Z' }],
  ['due delayed retry', v => { v.lifecycleDelivery.deliveryState = 'RetryScheduled'; v.lifecycleDelivery.attemptCount = 1; v.lifecycleDelivery.availableAt = now }],
  ['unconfirmed parent', v => { v.deliveryState = 'Queued'; v.deliveredAt = null; v.attemptCount = 0; v.lifecycleDelivery.attemptCount = 1 }],
] as [string, (v: ReturnType<typeof contextRow>) => void][])('rejects inconsistent row %s', (_, mutate) => {
  const value = contextRow(); mutate(value)
  expect(() => parseClosingReportPage(reportPage([value]), branch, {})).toThrow()
})
it('accepts matching list and detail receipts while rejecting contradictions outside the frozen snapshot', () => {
  const value = frozenLifecycleDetail()
  value.report = contextRow('Delivered')
  value.lifecycleDelivery = value.report.lifecycleDelivery
  expect(parseClosingReportDetail(value, branch, request).report.lifecycleDelivery).toEqual(value.lifecycleDelivery)
  const frozen = JSON.stringify(value.snapshot)
  expect(() => parseClosingReportDetail({ ...value, lifecycleDelivery: null }, branch, request)).toThrow()
  expect(() => parseClosingReportDetail({ ...value, report: { ...value.report, lifecycleDelivery: null } }, branch, request)).toThrow()
  expect(() => parseClosingReportDetail({ ...value, lifecycleDelivery: { ...value.lifecycleDelivery!, attemptCount: 2 } }, branch, request)).toThrow()
  expect(JSON.stringify(value.snapshot)).toBe(frozen)
  delete value.report.lifecycleDelivery
  expect(parseClosingReportDetail(value, branch, request).lifecycleDelivery?.deliveryState).toBe('Delivered')
})
