import type { createOrderingClient } from '../api/orderingClient.ts'
import type { ActiveTables, SessionDetail } from '../api/orderingContracts.ts'

export type OrderingReadClient = Pick<ReturnType<typeof createOrderingClient>, 'activeTables' | 'sessionDetail'>
export interface OrderingReadState {
  readonly tablesPhase: 'idle' | 'loading' | 'ready' | 'unavailable'
  readonly tables: ActiveTables['sessions']
  readonly selectedSessionId: string | null
  readonly detailPhase: 'idle' | 'loading' | 'ready' | 'unavailable' | 'notFound'
  readonly detail: SessionDetail | null
}

/** Read-only coordinator. Every refresh invalidates old financial content before awaiting the server. */
export function createOrderingReadController(client: OrderingReadClient) {
  let state: OrderingReadState = { tablesPhase: 'idle', tables: [], selectedSessionId: null, detailPhase: 'idle', detail: null }
  const listeners = new Set<() => void>()
  let tablesEpoch = 0, detailEpoch = 0
  let tablesAbort: AbortController | undefined, detailAbort: AbortController | undefined
  function update(patch: Partial<OrderingReadState>) { state = { ...state, ...patch }; for (const listener of listeners) listener() }
  function cancelDetail() { detailEpoch++; detailAbort?.abort() }
  async function selectSession(sessionId: string) {
    if (state.tablesPhase !== 'ready' || !state.tables.some(table => table.tableSessionId === sessionId)) return
    cancelDetail()
    const epoch = detailEpoch, abort = new AbortController()
    detailAbort = abort
    update({ selectedSessionId: sessionId, detailPhase: 'loading', detail: null })
    try {
      const result = await client.sessionDetail(sessionId, abort.signal)
      if (epoch !== detailEpoch || abort.signal.aborted) return
      if (result.kind === 'ok') update({ detailPhase: 'ready', detail: result.value })
      else update({ detailPhase: result.kind === 'notFound' ? 'notFound' : 'unavailable', detail: null })
    } catch {
      if (epoch === detailEpoch && !abort.signal.aborted) update({ detailPhase: 'unavailable', detail: null })
    }
  }
  async function refresh() {
    const selected = state.selectedSessionId
    tablesEpoch++; tablesAbort?.abort(); cancelDetail()
    const epoch = tablesEpoch, abort = new AbortController()
    tablesAbort = abort
    update({ tablesPhase: 'loading', tables: [], detailPhase: selected ? 'loading' : 'idle', detail: null })
    try {
      const result = await client.activeTables(abort.signal)
      if (epoch !== tablesEpoch || abort.signal.aborted) return
      if (result.kind !== 'ok') { update({ tablesPhase: 'unavailable', detailPhase: 'unavailable', detail: null }); return }
      update({ tablesPhase: 'ready', tables: result.value.sessions })
      if (selected && result.value.sessions.some(table => table.tableSessionId === selected)) await selectSession(selected)
      else update({ selectedSessionId: null, detailPhase: selected ? 'notFound' : 'idle', detail: null })
    } catch {
      if (epoch === tablesEpoch && !abort.signal.aborted) update({ tablesPhase: 'unavailable', detailPhase: 'unavailable', detail: null })
    }
  }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh, selectSession,
    cancel() { tablesEpoch++; tablesAbort?.abort(); cancelDetail() },
  }
}
