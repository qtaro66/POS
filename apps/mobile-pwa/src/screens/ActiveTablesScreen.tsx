import { useCallback, useEffect, useState } from 'react'
import type { ActiveTablesGateway } from '../adapters/ports.ts'
import { Time } from '../components/Time.tsx'
import { useFocusOnMount } from '../components/useFocusOnMount.ts'
import type { ActiveTablesSnapshot, EmployeeSession, PairedDevice } from '../domain/types.ts'
import { tableStatusText, text } from '../strings.ts'

interface ActiveTablesScreenProps {
  readonly device: PairedDevice
  readonly employee: EmployeeSession
  readonly tables: ActiveTablesGateway
  readonly onLogout: () => void
}

type TablesProblem = 'unreachable' | 'failed'

interface FetchResult {
  readonly snapshot: ActiveTablesSnapshot | null
  readonly problem: TablesProblem | null
}

/**
 * Read-only list of the branch's open table sessions. Mobile sees every open
 * session regardless of who opened it (requirement D10.4) and shows statuses
 * exactly as reported by the Branch Service.
 */
export function ActiveTablesScreen({ device, employee, tables, onLogout }: ActiveTablesScreenProps) {
  const headingRef = useFocusOnMount<HTMLHeadingElement>()
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

  const refresh = useCallback(async () => {
    setLoading(true)
    const result = await fetchTables()
    // A failed refresh keeps the last snapshot; it is shown together with the warning.
    if (result.snapshot) setSnapshot(result.snapshot)
    setProblem(result.problem)
    setLoading(false)
  }, [fetchTables])

  useEffect(() => {
    let active = true
    void fetchTables().then((result) => {
      if (!active) return
      if (result.snapshot) setSnapshot(result.snapshot)
      setProblem(result.problem)
      setLoading(false)
    })
    return () => {
      active = false
    }
  }, [fetchTables])

  return (
    <main className="screen" aria-busy={loading}>
      <div className="screen__header">
        <div>
          <h1 ref={headingRef} tabIndex={-1}>
            {text.tablesTitle}
          </h1>
          <p className="muted">{text.signedInAs(employee.displayName)}</p>
        </div>
        <button type="button" className="button" onClick={onLogout}>
          {text.logout}
        </button>
      </div>

      <div className="toolbar">
        <button
          type="button"
          className="button"
          onClick={() => void refresh()}
          disabled={loading}
        >
          {loading && snapshot ? text.refreshing : text.refresh}
        </button>
        {snapshot && (
          <span className="muted">
            {text.tablesReceivedAt} <Time value={snapshot.receivedAt} />
          </span>
        )}
      </div>

      {problem && (
        <div role="alert" className="alert">
          <p>{problem === 'unreachable' ? text.tablesUnreachable : text.tablesFailed}</p>
          {snapshot && <p>{text.tablesShowingLast}</p>}
        </div>
      )}

      {loading && !snapshot && <p className="muted">{text.tablesLoading}</p>}

      {snapshot && snapshot.tables.length === 0 && <p className="muted">{text.tablesEmpty}</p>}

      {snapshot && snapshot.tables.length > 0 && (
        <ul className="table-list" aria-label={text.tablesTitle}>
          {snapshot.tables.map((table) => (
            <li key={table.tableSessionId} className="table-card">
              <h2 className="table-card__label">{text.tableLabel(table.tableLabel)}</h2>
              <span className={`badge badge--${table.status}`}>
                {tableStatusText[table.status]}
              </span>
              <span className="muted table-card__opened">
                {text.tableOpenedAt} <Time value={table.openedAt} />
              </span>
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}
