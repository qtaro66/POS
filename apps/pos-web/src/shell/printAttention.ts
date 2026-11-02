import type { QueryResult } from '../api/orderingClient.ts'
import type { PrintAttentionPage } from '../api/printAttentionContracts.ts'

export interface PrintAttentionPort { printAttention?: (cursor?: string, signal?: AbortSignal) => Promise<QueryResult<PrintAttentionPage>> }
interface State { readonly phase: 'unsupported' | 'loading' | 'ready' | 'unavailable'; readonly page: PrintAttentionPage | null; readonly pageNumber: number }
function observationKey(value: string) {
  const [whole, fraction = ''] = value.replace(/(?:Z|\+00:00)$/, '').split('.')
  return `${whole}.${fraction.padEnd(7, '0')}`
}

/** Bounded, read-only pages. No dispatch, recovery, retry or printer access. */
export function createPrintAttention(port: PrintAttentionPort) {
  let state: State = { phase: port.printAttention ? 'loading' : 'unsupported', page: null, pageNumber: 1 }
  let epoch = 0, abort: AbortController | undefined
  const listeners = new Set<() => void>()
  function update(next: State) { state = next; for (const listener of listeners) listener() }
  function cancel() { epoch++; abort?.abort() }
  async function read(cursor?: string, pageNumber = 1, observedAt?: string) {
    cancel()
    if (!port.printAttention) { update({ phase: 'unsupported', page: null, pageNumber: 1 }); return }
    const current = epoch, controller = new AbortController()
    abort = controller
    update({ phase: 'loading', page: null, pageNumber })
    try {
      const result = await port.printAttention(cursor, controller.signal)
      if (current !== epoch || controller.signal.aborted) return
      const valid = result.kind === 'ok' && (cursor === undefined || result.value.nextCursor !== cursor)
        && (observedAt === undefined || observationKey(result.value.observedAt) === observationKey(observedAt))
      update({ phase: valid ? 'ready' : 'unavailable', page: valid ? result.value : null, pageNumber })
    } catch { if (current === epoch && !controller.signal.aborted) update({ phase: 'unavailable', page: null, pageNumber }) }
  }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh: () => read(), cancel,
    next() { return state.phase === 'ready' && state.page?.nextCursor ? read(state.page.nextCursor, state.pageNumber + 1, state.page.observedAt) : Promise.resolve() },
  }
}
