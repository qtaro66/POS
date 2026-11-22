import { afterEach, describe, expect, it, vi } from 'vitest'
import { createDataFreshness, observeDataFreshness, type FreshnessObservationHost } from './dataFreshness.ts'
import { at, deliveryView } from '../api/deliveryOverviewTestFixtures.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import type { DeliveryOverview } from '../api/deliveryOverviewContracts.ts'
const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }
async function settle() { for (let index = 0; index < 12; index++) await Promise.resolve() }
afterEach(() => vi.useRealTimers())

class Host implements FreshnessObservationHost {
  shown = true
  tasks = new Set<() => void>()
  visibilityListeners = new Set<() => void>()
  onlineListeners = new Set<() => void>()
  visible = () => this.shown
  visibility = (callback: () => void) => { this.visibilityListeners.add(callback); return () => { this.visibilityListeners.delete(callback) } }
  online = (callback: () => void) => { this.onlineListeners.add(callback); return () => { this.onlineListeners.delete(callback) } }
  schedule = (callback: () => void, delay: number) => { expect(delay).toBeGreaterThan(0); this.tasks.add(callback); return () => { this.tasks.delete(callback) } }
  tick() { const next = [...this.tasks]; this.tasks.clear(); next.forEach(callback => callback()) }
  show(value: boolean) { this.shown = value; this.visibilityListeners.forEach(callback => callback()) }
  reconnect() { this.onlineListeners.forEach(callback => callback()) }
}

describe('unified Data Sent observations', () => {
  it.each([[0, 1, 0, 0], [0, 0, 1, 0], [1, 0, 0, 0], [0, 0, 0, 1]])('keeps state pending with table/report/replay/context backlog %j/%j/%j/%j even with a recent acknowledgement', async (table, report, replay, context) => {
    const legacy = vi.fn(async () => ok(at))
    const controller = createDataFreshness({ dataSentStatus: legacy, deliveryOverview: async () => ok(deliveryView(table, report, replay, 1, context)) })
    await controller.refresh()
    const read = controller.getSnapshot().sent
    expect(read.phase).toBe('ready')
    if (read.phase === 'ready') { expect(read.timestamp).toBe(at); expect(read.overview?.snapshot.state).toBe('Pending') }
    expect(legacy).not.toHaveBeenCalled()
  })
  it('does not fall back to legacy success after a modern failure and can recover on a later read', async () => {
    const legacy = vi.fn(async () => ok(at))
    const modern = vi.fn().mockResolvedValueOnce({ kind: 'unavailable', reason: 'http', httpStatus: 404 }).mockResolvedValueOnce(ok(deliveryView(0, 0, 0, 1)))
    const controller = createDataFreshness({ dataSentStatus: legacy, deliveryOverview: modern })
    await controller.refresh(); expect(controller.getSnapshot().sent.phase).toBe('unavailable')
    await controller.refresh(); expect(controller.getSnapshot().sent.phase).toBe('ready')
    expect(legacy).not.toHaveBeenCalled()
  })
  it('retains legacy timestamps only when the modern port is absent and keeps synced independent', async () => {
    const controller = createDataFreshness({ dataSentStatus: async () => ok(at), dataSyncStatus: async () => ok(null) })
    await controller.refresh()
    expect(controller.getSnapshot()).toEqual({ synced: { phase: 'ready', timestamp: null }, sent: { phase: 'ready', timestamp: at } })
  })
  it('replaces obsolete reads and ignores late results from a cancelled scope', async () => {
    const first = deferred<QueryResult<DeliveryOverview>>()
    const modern = vi.fn().mockImplementationOnce(() => first.promise).mockResolvedValueOnce(ok(deliveryView(0, 1)))
    const controller = createDataFreshness({ deliveryOverview: modern })
    const old = controller.refresh()
    await controller.refresh()
    first.resolve(ok(deliveryView(0, 0, 0, 1))); await old; await settle()
    const reading = controller.getSnapshot().sent
    expect(reading.phase === 'ready' && reading.overview?.snapshot.state).toBe('Pending')
    controller.cancel()
  })
  it('bounds hanging observations and rejects a late success after timeout', async () => {
    vi.useFakeTimers()
    const delayed = deferred<QueryResult<DeliveryOverview>>()
    let signal: AbortSignal | undefined
    const controller = createDataFreshness({ deliveryOverview: async next => { signal = next; return delayed.promise } }, 100)
    const refresh = controller.refresh()
    await vi.advanceTimersByTimeAsync(100); await refresh
    expect(signal?.aborted).toBe(true)
    expect(controller.getSnapshot().sent.phase).toBe('unavailable')
    expect(controller.isRefreshing()).toBe(false)
    delayed.resolve(ok(deliveryView(0, 0, 0, 1))); await settle()
    expect(controller.getSnapshot().sent.phase).toBe('unavailable')
  })
  it('polls sequentially only while visible, replaces on reconnect, and disposes all listeners/timers', async () => {
    const host = new Host()
    const requests: ReturnType<typeof deferred<QueryResult<DeliveryOverview>>>[] = []
    let active = 0, maximum = 0
    const modern = vi.fn((signal?: AbortSignal) => {
      const next = deferred<QueryResult<DeliveryOverview>>(); requests.push(next)
      active++; maximum = Math.max(maximum, active)
      let finished = false
      const release = () => { if (!finished) { finished = true; active-- } }
      signal?.addEventListener('abort', release, { once: true })
      return next.promise.finally(release)
    })
    const controller = createDataFreshness({ deliveryOverview: modern })
    const stop = observeDataFreshness(controller, host, 100)
    expect(modern).toHaveBeenCalledTimes(1); expect(host.tasks.size).toBe(0)
    requests[0].resolve(ok(deliveryView(1))); await settle()
    expect(host.tasks.size).toBe(1)
    host.tick(); expect(modern).toHaveBeenCalledTimes(2)
    host.reconnect(); await settle(); expect(modern).toHaveBeenCalledTimes(3)
    host.show(false); await settle(); expect(active).toBe(0); expect(host.tasks.size).toBe(0)
    host.tick(); host.reconnect(); expect(modern).toHaveBeenCalledTimes(3)
    host.show(true); await settle(); expect(modern).toHaveBeenCalledTimes(4)
    stop(); await settle(); expect(active).toBe(0); expect(host.tasks.size).toBe(0)
    expect(host.visibilityListeners.size).toBe(0); expect(host.onlineListeners.size).toBe(0)
    host.tick(); host.reconnect(); expect(modern).toHaveBeenCalledTimes(4)
    expect(maximum).toBe(1)
  })
  it('does not overlap a manual accepted-command refresh with a scheduled poll', async () => {
    const host = new Host()
    const hanging = deferred<QueryResult<DeliveryOverview>>()
    const modern = vi.fn().mockResolvedValueOnce(ok(deliveryView())).mockImplementationOnce(() => hanging.promise).mockResolvedValue(ok(deliveryView(0, 1)))
    const controller = createDataFreshness({ deliveryOverview: modern })
    const stop = observeDataFreshness(controller, host, 100)
    await settle()
    const accepted = controller.refresh()
    host.tick(); expect(modern).toHaveBeenCalledTimes(2)
    hanging.resolve(ok(deliveryView(0, 1))); await accepted; await settle()
    host.tick(); await settle(); expect(modern).toHaveBeenCalledTimes(3)
    stop()
  })
})
