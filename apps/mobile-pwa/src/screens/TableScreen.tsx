import { Notice } from '../components/Messages.tsx'
import { Screen } from '../components/Screen.tsx'
import { Time } from '../components/Time.tsx'
import type { ActiveTables } from '../components/useActiveTables.ts'
import type { CurrentTable } from '../domain/shellRoute.ts'
import { tableStatusText, text } from '../strings.ts'

interface TableScreenProps {
  readonly table: CurrentTable
  readonly tables: ActiveTables
}

/**
 * The table the employee is working on. It shows only what the last Branch
 * list reports; order lines and bill totals need a Branch table-session
 * capability that does not exist yet (backlog MOB-07).
 */
export function TableScreen({ table, tables }: TableScreenProps) {
  const listed = tables.snapshot?.tables.find(
    (candidate) => candidate.tableSessionId === table.tableSessionId,
  )

  return (
    <Screen
      title={text.tableLabel(table.tableLabel)}
      subtitle={
        listed && (
          <span className={`badge badge--${listed.status}`}>{tableStatusText[listed.status]}</span>
        )
      }
    >
      {listed ? (
        <dl className="facts">
          <div>
            <dt>{text.tableOpenedAt}</dt>
            <dd>
              <Time value={listed.openedAt} />
            </dd>
          </div>
          {tables.snapshot && (
            <div>
              <dt>{text.tablesReceivedAt}</dt>
              <dd>
                <Time value={tables.snapshot.receivedAt} />
              </dd>
            </div>
          )}
        </dl>
      ) : (
        <Notice>
          <p>{text.tableNotListed(table.tableLabel)}</p>
        </Notice>
      )}
      <p className="muted">{text.tableDetailsUnavailable}</p>
    </Screen>
  )
}
