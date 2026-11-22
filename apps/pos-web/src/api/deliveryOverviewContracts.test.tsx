import { describe, expect, it } from 'vitest'
import { parseDeliveryOverview } from './deliveryOverviewContracts.ts'
import { at, branch, deliveryChannel, deliveryView } from './deliveryOverviewTestFixtures.ts'
import { createOrderingClient } from './orderingClient.ts'

const session = { serviceOrigin: 'https://branch.example.test', branchId: branch, deviceId: 'DEVICE-01', employeeId: 'EMP-01', deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

describe('unified delivery contract', () => {
  it.each([0, 1])('derives all three-source pending combinations with %i table items', tables => {
    for (const reports of [0, 1]) for (const replay of [0, 1]) {
      const value = deliveryView(tables, reports, replay, 2)
      expect(parseDeliveryOverview(value, branch).snapshot.state).toBe(tables || reports || replay ? 'Pending' : 'Current')
    }
  })
  it('distinguishes no history from current and includes replay confirmation in exact timestamp maximum', () => {
    expect(parseDeliveryOverview(deliveryView(), branch).snapshot.state).toBe('NoHistory')
    const value = deliveryView(0, 0, 0, 1)
    value.snapshot.tableEvents.lastConfirmedAt = '2026-11-21T03:00:00.1234560+00:00'
    value.snapshot.closingReports = deliveryChannel(0, 1)
    value.snapshot.closingReports.lastConfirmedAt = '2026-11-21T03:00:00.1234566Z'
    value.snapshot.replayCompletedCount = 1
    value.snapshot.replayLastConfirmedAt = at
    expect(parseDeliveryOverview(value, branch).snapshot.lastConfirmedAt).toBe(at)
    value.snapshot.lastConfirmedAt = '2026-11-21T03:00:00.1234566Z'
    expect(() => parseDeliveryOverview(value, branch)).toThrow()
  })
  it.each([
    ['schema version', (value: ReturnType<typeof deliveryView>) => { value.schemaVersion = 1 as 2 }],
    ['scope', (value: ReturnType<typeof deliveryView>) => { value.snapshot.branchId = 'OTHER' }],
    ['false current', (value: ReturnType<typeof deliveryView>) => { value.snapshot.state = 'Current' }],
    ['unsafe count', (value: ReturnType<typeof deliveryView>) => { value.snapshot.tableEvents.pendingCount = Number.MAX_SAFE_INTEGER + 1 }],
    ['count mismatch', (value: ReturnType<typeof deliveryView>) => { value.snapshot.tableEvents.totalCount = 20 }],
    ['overlapping active classifications', (value: ReturnType<typeof deliveryView>) => { value.snapshot.tableEvents.inFlightCount = 1; value.snapshot.tableEvents.delayedCount = 1 }],
    ['missing pending time', (value: ReturnType<typeof deliveryView>) => { value.snapshot.tableEvents.oldestPendingAt = null }],
    ['missing replay time', (value: ReturnType<typeof deliveryView>) => { value.snapshot.replayOldestPendingAt = null }],
    ['invalid UTC', (value: ReturnType<typeof deliveryView>) => { value.snapshot.asOf = '2026-11-21T10:00:00+07:00' }],
    ['fabricated confirmation', (value: ReturnType<typeof deliveryView>) => { value.snapshot.lastConfirmedAt = at }],
  ])('rejects %s instead of fabricating an aggregate state', (_name, mutate) => {
    const value = deliveryView(1, 0, 1)
    mutate(value)
    expect(() => parseDeliveryOverview(value, branch)).toThrow()
  })
  it('projects only public fields and allows unknown count overlap while keeping lease classes disjoint', () => {
    const value = deliveryView(2)
    value.snapshot.tableEvents.unknownCount = 2
    value.snapshot.tableEvents.inFlightCount = 1
    value.snapshot.tableEvents.expiredLeaseCount = 1
    const projected = parseDeliveryOverview({ ...value, rawToken: 'private', snapshot: { ...value.snapshot, worker: 'private', tableEvents: { ...value.snapshot.tableEvents, rawFailure: 'private' } } }, branch)
    expect(JSON.stringify(projected)).not.toContain('private')
    expect(projected.snapshot.tableEvents.unknownCount).toBe(2)
  })
  it('uses an authenticated GET and rejects modern 404 without substituting legacy timestamps', async () => {
    let request: RequestInit | undefined
    let requested = ''
    const client = createOrderingClient(session, { fetch: async (url, init) => { requested = url; request = init; return json(deliveryView(0, 1, 0, 1)) } })
    const result = await client.deliveryOverview()
    expect(result.kind).toBe('ok')
    expect(requested).toBe('https://branch.example.test/api/v1/ordering/delivery-overview')
    expect(request?.method).toBe('GET')
    expect(request?.body).toBeUndefined()
    expect(new Headers(request?.headers).get('Authorization')).toBeTruthy()
    const absent = createOrderingClient(session, { fetch: async () => json({ error: 'NotFound' }, 404) })
    expect((await absent.deliveryOverview()).kind).toBe('unavailable')
  })
  it('treats foreign protocol responses and partial storage failures as unavailable', async () => {
    const client = createOrderingClient(session, { fetch: async () => json(deliveryView(1), 503) })
    expect((await client.deliveryOverview()).kind).toBe('unavailable')
    const foreign = deliveryView(); foreign.snapshot.branchId = 'OTHER'
    const forged = createOrderingClient(session, { fetch: async () => json(foreign) })
    expect((await forged.deliveryOverview()).kind).toBe('unavailable')
  })
})
