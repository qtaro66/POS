import type { createCommandRecovery } from './commandRecovery.ts'
import type { JournalEntry } from './commandJournal.ts'
import { validateCommandSnapshot } from '../api/orderingClient.ts'

export type RecoveryReadPort = Pick<ReturnType<typeof createCommandRecovery>, 'list' | 'recover'>
type Lookup = Awaited<ReturnType<RecoveryReadPort['recover']>>
export interface RecoveryRow {
  readonly entry: JournalEntry
  readonly phase: 'unchecked' | 'checking' | 'checked' | 'unavailable'
  readonly outcome?: Lookup
  readonly checkedAt?: string
}
export interface RecoveryState {
  readonly phase: 'idle' | 'loading' | 'ready' | 'unavailable'
  readonly rows: readonly RecoveryRow[]
}

/** Status reads only. No retry/finalize capability is passed to this controller. */
export function createRecoveryController(port: RecoveryReadPort, now = () => new Date()) {
  let state: RecoveryState = { phase: 'idle', rows: [] }, epoch = 0
  const listeners = new Set<() => void>(), checks = new Map<string, AbortController>()
  const update = (next: RecoveryState) => { state = next; for (const listener of listeners) listener() }
  function cancel() { epoch++; for (const check of checks.values()) check.abort(); checks.clear() }
  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    cancel,
    async refresh() {
      cancel()
      const current = epoch
      update({ phase: 'loading', rows: [] })
      try {
        const ids = new Set<string>()
        const entries = (await port.list()).map(entry => {
          const command = validateCommandSnapshot(entry.command)
          if (!['pending', 'resolved'].includes(entry.state) || ids.has(command.requestId)) throw new Error('Invalid recovery entries.')
          ids.add(command.requestId)
          return { command, state: entry.state }
        })
        if (current !== epoch) return
        update({ phase: 'ready', rows: entries.map(entry => ({ entry, phase: 'unchecked' })) })
      } catch { if (current === epoch) update({ phase: 'unavailable', rows: [] }) }
    },
    async check(requestId: string) {
      if (state.phase !== 'ready' || !state.rows.some(row => row.entry.command.requestId === requestId)) return
      const current = epoch, abort = new AbortController()
      checks.get(requestId)?.abort(); checks.set(requestId, abort)
      const replace = (patch: Omit<RecoveryRow, 'entry'>) => update({ ...state, rows: state.rows.map(row => row.entry.command.requestId === requestId ? { entry: row.entry, ...patch } : row) })
      replace({ phase: 'checking' })
      try {
        const outcome = await port.recover(requestId, abort.signal)
        if (current !== epoch || abort.signal.aborted) return
        replace({ phase: outcome.result.kind === 'unavailable' ? 'unavailable' : 'checked', outcome, checkedAt: now().toISOString() })
      } catch {
        if (current === epoch && !abort.signal.aborted) replace({ phase: 'unavailable' })
      } finally { if (checks.get(requestId) === abort) checks.delete(requestId) }
    },
  }
}
