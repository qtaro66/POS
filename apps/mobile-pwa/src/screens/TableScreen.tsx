import type { ReactNode } from 'react'
import type { DraftRepository } from '../adapters/ports.ts'
import { Basket } from '../components/Basket.tsx'
import { LiveMessage } from '../components/LiveMessage.tsx'
import { Notice } from '../components/Messages.tsx'
import { Screen } from '../components/Screen.tsx'
import { Time } from '../components/Time.tsx'
import type { ActiveTables } from '../components/useActiveTables.ts'
import type { DraftData } from '../components/useDraft.ts'
import type { DraftLine, FulfillmentType } from '../domain/draft.ts'
import type { Menu } from '../domain/menu.ts'
import type { CurrentTable } from '../domain/shellRoute.ts'
import { tableStatusText, text } from '../strings.ts'

interface TableScreenProps {
  readonly sending?: boolean
  readonly onSend?: () => void
  readonly detail?: ReactNode
  readonly table: CurrentTable
  readonly tables: ActiveTables
  readonly draft: DraftData
  readonly persistence: DraftRepository['persistence']
  readonly menu: Menu | null
  /** Confirmation of the last basket change, announced politely. */
  readonly message: string | null
  readonly onAddItems: () => void
  readonly onQuantity: (line: DraftLine, quantity: number) => void
  readonly onFulfillment: (line: DraftLine, fulfillmentType: FulfillmentType) => void
  readonly onEdit: (line: DraftLine) => void
  readonly onRemove: (line: DraftLine) => void
}

/**
 * The table the employee is working on: what the last Branch list reports
 * and this device's basket. Accepted order lines and bill totals come from the Branch. Items can
 * be added only while the last list reports the session as `Ordering`, because
 * the Branch locks the bill from `PaymentPending` on (D03, D20.2).
 */
export function TableScreen({
  table,
  detail,
  sending,
  onSend,
  tables,
  draft,
  persistence,
  menu,
  message,
  onAddItems,
  onQuantity,
  onEdit,
  onFulfillment,
  onRemove,
}: TableScreenProps) {
  const listed = tables.snapshot?.tables.find(
    (candidate) => candidate.tableSessionId === table.tableSessionId,
  )
  const closed = !listed && tables.snapshot !== null && !tables.loading && !tables.problem
  const ordering = listed?.status === 'Ordering'

  return (
    <Screen
      title={text.tableLabel(table.tableLabel)}
      subtitle={
        listed && (
          <span className={`badge badge--${listed.status}`}>{tableStatusText[listed.status]}</span>
        )
      }
    >
      <LiveMessage message={message} />
      {closed ? (
        <p role="status">{text.tableSessionClosed}</p>
      ) : listed ? (
        <dl className="facts">
          {listed.openedAt && <div>
            <dt>{text.tableOpenedAt}</dt>
            <dd>
              <Time value={listed.openedAt} />
            </dd>
          </div>}
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

      {ordering ? (
        <div className="toolbar">
          <button type="button" className="button button--primary" onClick={onAddItems}>
            {text.addItems}
          </button>
        </div>
      ) : (
        listed && <p className="muted">{text.addItemsNotOrdering}</p>
      )}

      {!closed && draft.loaded && !sending && (
        <Basket
          lines={draft.draft?.lines ?? []}
          rejection={draft.draft?.rejection}
          menu={menu}
          persistence={persistence}
          saving={draft.saving}
          saveFailed={draft.saveFailed}
          needsReview={draft.draft?.reviewState === 'ExpiredNeedsReview'}
          onQuantity={onQuantity}
          onEdit={onEdit}
          onFulfillment={onFulfillment}
          onRemove={onRemove}
        />
      )}
      {ordering && onSend && !!draft.draft?.lines.length && (
        <button type="button" className="button button--primary" onClick={onSend}
          disabled={sending || draft.saving || draft.saveFailed || draft.draft.reviewState === 'ExpiredNeedsReview'}>
          {sending ? 'Sending…' : 'Send order'}
        </button>
      )}
      {!closed && (detail || <p className="muted">{text.tableDetailsUnavailable}</p>)}
    </Screen>
  )
}
