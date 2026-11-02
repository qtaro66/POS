import type { QueryResult } from '../api/orderingClient.ts'

export interface FreshnessPort {
  dataSyncStatus?: (signal?: AbortSignal) => Promise<QueryResult<string | null>>
  dataSentStatus?: (signal?: AbortSignal) => Promise<QueryResult<string | null>>
}
export type FreshnessReading = { phase: 'unsupported' | 'loading' | 'unavailable' } | { phase: 'ready'; timestamp: string | null }
export interface FreshnessState { synced: FreshnessReading; sent: FreshnessReading }

/** Authenticated Branch observations, not a heartbeat or a claim of overall system health. */
export function createDataFreshness(port: FreshnessPort) {
  let state: FreshnessState = {
    synced: { phase: port.dataSyncStatus ? 'loading' : 'unsupported' },
    sent: { phase: port.dataSentStatus ? 'loading' : 'unsupported' },
  }
  let epoch = 0, abort: AbortController | undefined
  const listeners = new Set<() => void>()
  function update(key: keyof FreshnessState, reading: FreshnessReading) {
    state = { ...state, [key]: reading }
    for (const listener of listeners) listener()
  }
  function cancel() { epoch++; abort?.abort() }
  async function refresh() {
    cancel()
    const current = epoch, controller = new AbortController()
    abort = controller
    await Promise.all((['synced', 'sent'] as const).map(async key => {
      const read = key === 'synced' ? port.dataSyncStatus : port.dataSentStatus
      if (!read) { update(key, { phase: 'unsupported' }); return }
      update(key, { phase: 'loading' })
      try {
        const result = await read.call(port, controller.signal)
        if (epoch === current && !controller.signal.aborted) update(key, result.kind === 'ok' ? { phase: 'ready', timestamp: result.value } : { phase: 'unavailable' })
      } catch { if (epoch === current && !controller.signal.aborted) update(key, { phase: 'unavailable' }) }
    }))
  }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh, cancel,
  }
}
