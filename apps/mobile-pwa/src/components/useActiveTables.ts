import { useCallback, useEffect, useState } from 'react'
import type { ActiveTablesGateway } from '../adapters/ports.ts'
import type { ActiveTablesSnapshot, PairedDevice } from '../domain/types.ts'

export type TablesProblem = 'unreachable' | 'failed'

interface FetchResult {
  readonly snapshot: ActiveTablesSnapshot | null
  readonly problem: TablesProblem | null
}

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
export function useActiveTables(device: PairedDevice, tables: ActiveTablesGateway): ActiveTables {
  const [snapshot, setSnapshot] = useState<ActiveTablesSnapshot | null>(null)
  const [loading, setLoading] = useState(true)
  const [problem, setProblem] = useState<TablesProblem | null>(null)

  const fetchTables = useCallback(async (): Promise<FetchResult> => {
    try {
      const outcome = await tables.listActiveTables(device)
      return outcome.kind === 'ok'
        ? { snapshot: outcome.snapshot, problem: null }
        : { snapshot: null, problem: 'unreachable' }
    } catch {
      return { snapshot: null, problem: 'failed' }
    }
  }, [device, tables])

  const apply = useCallback((result: FetchResult) => {
    // A failed refresh keeps the last snapshot; it is shown together with the warning.
    if (result.snapshot) setSnapshot(result.snapshot)
    setProblem(result.problem)
    setLoading(false)
  }, [])

  const refresh = useCallback(() => {
    setLoading(true)
    void fetchTables().then(apply)
  }, [apply, fetchTables])

  useEffect(() => {
    let active = true
    void fetchTables().then((result) => {
      if (active) apply(result)
    })
    return () => {
      active = false
    }
  }, [apply, fetchTables])

  return { snapshot, loading, problem, refresh }
}
