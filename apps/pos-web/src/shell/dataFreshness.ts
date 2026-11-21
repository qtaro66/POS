import type { QueryResult } from '../api/orderingClient.ts'
import type { DeliveryOverview } from '../api/deliveryOverviewContracts.ts'

export interface FreshnessPort {
  dataSyncStatus?: (signal?: AbortSignal) => Promise<QueryResult<string | null>>
  dataSentStatus?: (signal?: AbortSignal) => Promise<QueryResult<string | null>>
  deliveryOverview?: (signal?: AbortSignal) => Promise<QueryResult<DeliveryOverview>>
}
export type FreshnessReading = { phase: 'unsupported' | 'loading' | 'unavailable' } |
  { phase: 'ready'; timestamp: string | null; overview?: DeliveryOverview }
export interface FreshnessState { synced: FreshnessReading; sent: FreshnessReading }

/** Authenticated Branch observations, not a heartbeat or a claim of overall system health. */
export function createDataFreshness(port: FreshnessPort, timeoutMs = 10_000) {
  let state: FreshnessState = {
    synced: { phase: port.dataSyncStatus ? 'loading' : 'unsupported' },
    sent: { phase: port.deliveryOverview || port.dataSentStatus ? 'loading' : 'unsupported' },
  }
  let epoch = 0, abort: AbortController | undefined, inFlight = false
  const listeners = new Set<() => void>()
  function update(key: keyof FreshnessState, reading: FreshnessReading) {
    state = { ...state, [key]: reading }
    for (const listener of listeners) listener()
  }
  function cancel() { epoch++; abort?.abort(); inFlight = false }
  async function refresh() {
    cancel()
    const current = epoch, controller = new AbortController()
    abort = controller; inFlight = true
    let stop!: () => void
    const cancelled = new Promise<void>(resolve => { stop = resolve; controller.signal.addEventListener('abort', () => resolve(), { once: true }) })
    const deadline = setTimeout(() => {
      if (epoch !== current) return
      for (const key of ['synced', 'sent'] as const) if (state[key].phase === 'loading') update(key, { phase: 'unavailable' })
      controller.abort()
    }, timeoutMs)
    const work = Promise.all((['synced', 'sent'] as const).map(async key => {
      const read: ((signal?: AbortSignal) => Promise<QueryResult<string | null | DeliveryOverview>>) | undefined = key === 'synced' ? port.dataSyncStatus : port.deliveryOverview ?? port.dataSentStatus
      if (!read) { update(key, { phase: 'unsupported' }); return }
      update(key, { phase: 'loading' })
      try {
        const result = await read.call(port, controller.signal)
        if (epoch !== current || controller.signal.aborted) return
        if (result.kind !== 'ok') { update(key, { phase: 'unavailable' }); return }
        if (key === 'sent' && port.deliveryOverview) {
          const overview = result.value as DeliveryOverview
          update(key, { phase: 'ready', timestamp: overview.snapshot.lastConfirmedAt, overview })
        } else update(key, { phase: 'ready', timestamp: result.value as string | null })
      } catch { if (epoch === current && !controller.signal.aborted) update(key, { phase: 'unavailable' }) }
    }))
    try { await Promise.race([work, cancelled]) }
    finally { clearTimeout(deadline); stop(); if (epoch === current) inFlight = false }
  }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh, cancel, isRefreshing: () => inFlight,
  }
}

export interface FreshnessObservationHost {
  visible(): boolean
  visibility(listener: () => void): () => void
  online(listener: () => void): () => void
  schedule(listener: () => void, delay: number): () => void
}
export function browserFreshnessHost(): FreshnessObservationHost {
  return {
    visible: () => document.visibilityState === 'visible',
    visibility(listener) { document.addEventListener('visibilitychange', listener); return () => document.removeEventListener('visibilitychange', listener) },
    online(listener) { window.addEventListener('online', listener); return () => window.removeEventListener('online', listener) },
    schedule(listener, delay) { const timer = setTimeout(listener, delay); return () => clearTimeout(timer) },
  }
}
/** Sequential visible-page polling; reconnect explicitly replaces obsolete reads. */
export function observeDataFreshness(reading: ReturnType<typeof createDataFreshness>, host: FreshnessObservationHost, intervalMs = 30_000) {
  let stopped = false, running = false, requested = false, cancelTimer: (() => void) | undefined
  function schedule() { cancelTimer?.(); if (!stopped && host.visible()) cancelTimer = host.schedule(() => { void run() }, intervalMs) }
  async function run() {
    if (stopped || !host.visible()) return
    if (running) { requested = true; reading.cancel(); return }
    if (reading.isRefreshing()) { schedule(); return }
    cancelTimer?.(); running = true
    try { await reading.refresh() }
    finally {
      running = false
      if (!stopped && host.visible()) { if (requested) { requested = false; void run() } else schedule() }
    }
  }
  function visibility() {
    cancelTimer?.()
    if (!host.visible()) { requested = false; reading.cancel(); return }
    void run()
  }
  const unsubscribeVisibility = host.visibility(visibility)
  const unsubscribeOnline = host.online(() => { if (host.visible()) { reading.cancel(); void run() } })
  void run()
  return () => { stopped = true; cancelTimer?.(); unsubscribeVisibility(); unsubscribeOnline(); reading.cancel() }
}
