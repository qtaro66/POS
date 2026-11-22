import { expect, it } from 'vitest'
import { createOrderingClient, type OrderingSession } from './orderingClient.ts'
import { validateClosingReportQuery, type ClosingLifecycleFilter } from './closingReportWorkspaceContracts.ts'
import { branch, reportItem, reportPage } from './closingReportTestFixtures.ts'

it('preserves independent context filter and cursor on authenticated report requests', async () => {
  const urls: URL[] = []
  const session: OrderingSession = { serviceOrigin: 'https://branch.example', branchId: branch, deviceId: 'device', employeeId: 'employee', deviceCredential: 'credential', employeeSessionToken: 'token' }
  const client = createOrderingClient(session, { fetch: (async (url: string | URL | Request) => {
    urls.push(new URL(String(url)))
    const item = reportItem({ deliveryState: 'Delivered', deliveredAt: '2026-11-21T03:00:00Z', attemptCount: 1 })
    const filter = urls.at(-1)?.searchParams.get('lifecycleStatus')
    if (filter === 'NotRecorded') item.lifecycleDelivery = null
    else if (filter !== 'All') item.lifecycleDelivery = { contextId: 'lifecycle:' + item.reportId,
      deliveryState: filter === 'Delivered' ? 'Delivered' : 'Queued', attemptCount: filter === 'Delivered' ? 1 : 0,
      availableAt: item.availableAt, leaseExpiresAt: null, deliveredAt: filter === 'Delivered' ? item.deliveredAt : null, lastFailure: null }
    return new Response(JSON.stringify(reportPage([item])), { status: 200, headers: { 'Content-Type': 'application/json' } })
  }) as typeof fetch })
  for (const lifecycleStatus of ['All', 'Pending', 'Delivered', 'NotRecorded'] as ClosingLifecycleFilter[]) {
    expect((await client.closingReports({ status: 'Delivered', lifecycleStatus, cursor: 'boundcursor' })).kind).toBe('ok')
    expect(urls.at(-1)?.searchParams.get('status')).toBe('Delivered')
    expect(urls.at(-1)?.searchParams.get('lifecycleStatus')).toBe(lifecycleStatus)
    expect(urls.at(-1)?.searchParams.get('cursor')).toBe('boundcursor')
  }
  const count = urls.length
  for (const invalid of ['pending', '', 'Unknown', ' Delivered ', null, 1]) {
    expect(() => validateClosingReportQuery({ lifecycleStatus: invalid as never })).toThrow()
    expect((await client.closingReports({ lifecycleStatus: invalid as never })).kind).toBe('unavailable')
  }
  expect(urls).toHaveLength(count)
})
