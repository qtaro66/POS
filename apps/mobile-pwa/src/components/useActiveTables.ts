import { useCallback, useEffect, useMemo, useState } from 'react'
import type { ActiveTablesGateway } from '../adapters/ports.ts'
import type { ActiveTablesSnapshot, EmployeeSession, PairedDevice } from '../domain/types.ts'

export type TablesProblem = 'unreachable' | 'failed'

export interface ActiveTables {
  /** Last list received from the Branch Service; kept when a later refresh fails. */
  readonly snapshot: ActiveTablesSnapshot | null
  readonly loading: boolean
  readonly problem: TablesProblem | null
  readonly refresh: () => void
}

/**
 * Loads the branch's open table sessions once per login and on request. The
 * shell owns the result so every screen shows the same last-known list.
 */
export function useActiveTables(device: PairedDevice, tables: ActiveTablesGateway, employee: EmployeeSession): ActiveTables {
  const scope = useMemo(() => ({ device, employee, tables }), [device, employee, tables])
  const [attempt, setAttempt] = useState(0)
  const request = useMemo(() => ({ scope, attempt }), [scope, attempt])
  const [result, setResult] = useState<{
    request: typeof request; snapshot: ActiveTablesSnapshot | null; problem: TablesProblem | null
  }>()
  const refresh = useCallback(() => setAttempt(count => count + 1), [])

  useEffect(() => {
    const controller = new AbortController()
    function apply(snapshot: ActiveTablesSnapshot | null, problem: TablesProblem | null) {
      if (controller.signal.aborted) return
      setResult(previous => ({
        request,
        snapshot: snapshot ?? (previous?.request.scope === scope ? previous.snapshot : null),
        problem,
      }))
    }
    void (async () => {
      try {
        const outcome = await tables.listActiveTables(device, employee, controller.signal)
        if (outcome.kind === 'ok') apply(outcome.snapshot, null)
        else apply(null, 'unreachable')
      } catch { apply(null, 'failed') }
    })()
    return () => controller.abort()
  }, [device, employee, request, scope, tables])

  return {
    snapshot: result?.request.scope === scope ? result.snapshot : null,
    loading: result?.request !== request,
    problem: result?.request === request ? result.problem : null,
    refresh,
  }
}
