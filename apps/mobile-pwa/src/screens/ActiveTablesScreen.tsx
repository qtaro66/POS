import { Alert } from '../components/Messages.tsx'
import { Screen } from '../components/Screen.tsx'
import { Time } from '../components/Time.tsx'
import type { ActiveTables } from '../components/useActiveTables.ts'
import type { CurrentTable } from '../domain/shellRoute.ts'
import type { EmployeeSession } from '../domain/types.ts'
import { tableStatusText, text } from '../strings.ts'

interface ActiveTablesScreenProps {
  readonly employee: EmployeeSession
  readonly tables: ActiveTables
  readonly onOpenTable: (table: CurrentTable) => void
}

/**
 * Read-only list of the branch's open table sessions. Mobile sees every open
 * session regardless of who opened it (requirement D10.4) and shows statuses
 * exactly as reported by the Branch Service.
 */
export function ActiveTablesScreen({ employee, tables, onOpenTable }: ActiveTablesScreenProps) {
  const { snapshot, loading, problem, refresh } = tables

  return (
    <Screen
      title={text.tablesTitle}
      busy={loading}
      subtitle={<p className="muted">{text.signedInAs(employee.displayName)}</p>}
    >
      <div className="toolbar">
        <button type="button" className="button" onClick={refresh} disabled={loading}>
          {loading && snapshot ? text.refreshing : text.refresh}
        </button>
        {snapshot && (
          <span className="muted">
            {text.tablesReceivedAt} <Time value={snapshot.receivedAt} />
          </span>
        )}
      </div>

      {problem && (
        <Alert>
          <p>{problem === 'unreachable' ? text.tablesUnreachable : text.tablesFailed}</p>
          {snapshot && <p>{text.tablesShowingLast}</p>}
        </Alert>
      )}

      {loading && !snapshot && <p className="muted">{text.tablesLoading}</p>}

      {snapshot && snapshot.tables.length === 0 && <p className="muted">{text.tablesEmpty}</p>}

      {snapshot && snapshot.tables.length > 0 && (
        <ul className="table-list" aria-label={text.tablesTitle}>
          {snapshot.tables.map((table) => (
            <li key={table.tableSessionId} className="table-card">
              <h2 className="table-card__label">
                {/* The button covers the whole card (see App.css), so the card is one large target. */}
                <button
                  type="button"
                  className="table-card__open"
                  onClick={() =>
                    onOpenTable({
                      tableSessionId: table.tableSessionId,
                      tableLabel: table.tableLabel,
                    })
                  }
                >
                  {text.tableLabel(table.tableLabel)}
                </button>
              </h2>
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
    </Screen>
  )
}
