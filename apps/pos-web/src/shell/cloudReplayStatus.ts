import type { QueryResult } from '../api/orderingClient.ts'
import type { CloudReplayPage } from '../api/cloudReplayContracts.ts'

export interface CloudReplayPort { cloudReplayStatus?: (after?: string, signal?: AbortSignal) => Promise<QueryResult<CloudReplayPage>> }
interface State { readonly phase: 'unsupported' | 'loading' | 'ready' | 'unavailable'; readonly page: CloudReplayPage | null; readonly pageNumber: number }
export function createCloudReplayStatus(port: CloudReplayPort) {
  let state: State = { phase: port.cloudReplayStatus ? 'loading' : 'unsupported', page: null, pageNumber: 1 }
  let epoch = 0, abort: AbortController | undefined
  const listeners = new Set<() => void>()
  function update(next: State) { state = next; for (const listener of listeners) listener() }
  function cancel() { epoch++; abort?.abort() }
  async function read(after?: string, pageNumber = 1) {
    cancel()
    if (!port.cloudReplayStatus) { update({ phase: 'unsupported', page: null, pageNumber: 1 }); return }
    const current = epoch, controller = new AbortController()
    abort = controller; update({ phase: 'loading', page: null, pageNumber })
    try {
      const result = await port.cloudReplayStatus(after, controller.signal)
      if (current !== epoch || controller.signal.aborted) return
      update({ phase: result.kind === 'ok' ? 'ready' : 'unavailable', page: result.kind === 'ok' ? result.value : null, pageNumber })
    } catch { if (current === epoch && !controller.signal.aborted) update({ phase: 'unavailable', page: null, pageNumber }) }
  }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh: () => read(), cancel,
    next() { return state.phase === 'ready' && state.page?.nextAfterSequence ? read(state.page.nextAfterSequence, state.pageNumber + 1) : Promise.resolve() },
  }
}
