import { useEffect, useRef, useState } from 'react'
import { processOrders } from '../adapters/processOrders.ts'
import type { MobileServices } from '../adapters/ports.ts'
import { BottomNav } from '../components/BottomNav.tsx'
import { Notice } from '../components/Messages.tsx'
import { Screen } from '../components/Screen.tsx'
import { useActiveTables } from '../components/useActiveTables.ts'
import { useDraft } from '../components/useDraft.ts'
import { useMenu } from '../components/useMenu.ts'
import { useShellNavigation } from '../components/useShellNavigation.ts'
import {
  addLine,
  countItems,
  removeLine,
  replaceLine,
  setLineQuantity,
  type DraftLine,
} from '../domain/draft.ts'
import { findItem, type MenuItem } from '../domain/menu.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'
import { ActiveTablesScreen } from './ActiveTablesScreen.tsx'
import { ItemScreen } from './ItemScreen.tsx'
import { MenuScreen } from './MenuScreen.tsx'
import { StatusScreen } from './StatusScreen.tsx'
import { TableDetailPanel } from './TableDetailPanel.tsx'
import { TableScreen } from './TableScreen.tsx'

interface AuthenticatedShellProps {
  readonly device: PairedDevice
  readonly employee: EmployeeSession
  readonly services: Pick<MobileServices, 'tables' | 'menu' | 'drafts' | 'syncStatus' | 'soldOut' | 'outbox' | 'submitOrder'>
  /** Device clock for Draft timestamps and the 30-day review rule; must keep its identity. */
  readonly now: () => Date
  /** Creates basket line identifiers; replaced in tests. */
  readonly newLineId?: () => string
}

const randomLineId = () => crypto.randomUUID()

/**
 * Screens available to a logged-in employee, with the bottom navigation.
 * Each screen is keyed by its route so that navigating remounts it and moves
 * focus to its heading. The table list, the current table's menu and its
 * basket are loaded here and shared by the screens.
 */
export function AuthenticatedShell({
  device,
  employee,
  services,
  now,
  newLineId = randomLineId,
}: AuthenticatedShellProps) {
  const [sending, setSending] = useState(false)
  const sendingRef = useRef(false)
  const [message, setMessage] = useState<string | null>(null)
  // Confirmations belong to the table's screens; leaving them clears the last one.
  const { route, currentTable, navigate, back } = useShellNavigation((next) => {
    if (next.screen === 'tables' || next.screen === 'status') setMessage(null)
  })
  const [opening, setOpening] = useState(false)
  const [openFailed, setOpenFailed] = useState(false)
  const openInFlight = useRef(false)
  const pendingOpen = useRef(new Map<string, { requestId: string; tableSessionId: string; clientCreatedAt: string }>())
  const tables = useActiveTables(device, services.tables, employee)
  const refreshTables = tables.refresh
  const openTableSessionId = route.screen === 'table' ? route.table.tableSessionId : null
  useEffect(() => {
    if (!openTableSessionId) return
    const timer = window.setInterval(refreshTables, 15_000)
    return () => window.clearInterval(timer)
  }, [openTableSessionId, refreshTables])
  const tableSessionId = currentTable?.tableSessionId ?? null
  const menu = useMenu(services.menu, device, tableSessionId, employee)
  const readyMenu = menu.state.status === 'ready' ? menu.state.menu : null
  const draft = useDraft(
    services.drafts,
    tableSessionId
      ? { deviceId: device.deviceId, employeeId: employee.employeeId, tableSessionId }
      : null,
    now,
  )

  async function openAvailable(table: { tableId: string; tableLabel: string }) {
    if (!services.tables.openTable || openInFlight.current) return
    openInFlight.current = true
    setOpening(true)
    setOpenFailed(false)
    const context = pendingOpen.current.get(table.tableId) ?? {
      requestId: crypto.randomUUID(), tableSessionId: crypto.randomUUID(), clientCreatedAt: now().toISOString(),
    }
    pendingOpen.current.set(table.tableId, context)
    try {
      const tableSessionId = await services.tables.openTable(device, employee, table.tableId, context)
      pendingOpen.current.delete(table.tableId)
      tables.refresh()
      navigate({ screen: 'table', table: { tableSessionId, tableLabel: table.tableLabel } })
    } catch {
      setOpenFailed(true)
    } finally {
      openInFlight.current = false
      setOpening(false)
    }
  }

  const reloadDraft = draft.reload
  useEffect(() => {
    if (!services.outbox || !services.submitOrder) return
    const controller = new AbortController()
    function resume() {
      if (document.visibilityState === 'hidden') return
      void processOrders(services, device, employee, now, undefined, controller.signal)
        .then(() => { reloadDraft(); refreshTables() }, () => undefined)
    }
    resume()
    document.addEventListener('visibilitychange', resume)
    window.addEventListener('online', resume)
    return () => {
      controller.abort()
      document.removeEventListener('visibilitychange', resume)
      window.removeEventListener('online', resume)
    }
  }, [services, device, employee, now, reloadDraft, refreshTables])

  async function sendOrder() {
    if (sendingRef.current || !draft.draft || draft.saving || draft.saveFailed) return
    sendingRef.current = true
    setSending(true)
    try {
      const state = await processOrders(services, device, employee, now, draft.draft)
      setMessage(state === 'BranchAccepted' ? 'Order accepted by the branch.'
        : state === 'Rejected' ? 'Order rejected by the branch. Review the saved items before sending again.'
        : 'Order saved on this device. Branch confirmation is pending.')
      if (state === 'BranchAccepted') tables.refresh()
    } catch {
      setMessage('Order status could not be confirmed. Check the outbox before trying again.')
    } finally {
      reloadDraft()
      sendingRef.current = false
      setSending(false)
    }
  }

  async function requestCancellation(lines: readonly import('../domain/mobileRequest.ts').CancellationSelection[]) {
    if (!tableSessionId || sendingRef.current) return false
    sendingRef.current = true
    setSending(true)
    try {
      const state = await processOrders(services, device, employee, now, undefined, undefined, undefined, { tableSessionId, lines })
      setMessage(state === 'BranchAccepted' ? 'Cancellation request sent to the cashier.'
        : state === 'Rejected' ? 'Cancellation request rejected. Check the available quantities.'
        : 'Cancellation request status is unconfirmed. Check the outbox.')
      return state === 'BranchAccepted'
    } catch {
      setMessage('Cancellation request status could not be confirmed. Check the outbox.')
      return false
    } finally {
      tables.refresh()
      sendingRef.current = false
      setSending(false)
    }
  }

  async function requestPayment() {
    const table = tables.snapshot?.tables.find(row => row.tableSessionId === tableSessionId)
    if (!table || table.status !== 'Ordering' || draft.draft?.lines.length || sendingRef.current) return
    sendingRef.current = true
    setSending(true)
    try {
      const result = await processOrders(services, device, employee, now, undefined, undefined, {
        tableSessionId: table.tableSessionId, version: table.tableVersion,
      })
      setMessage(result === 'BranchAccepted' ? 'Payment requested.'
        : result === 'Rejected' ? 'The branch rejected the payment request. Check the latest table status.'
        : 'Payment request status is unconfirmed. Check the outbox.')
    } catch {
      setMessage('Payment request status could not be confirmed. Check the outbox.')
    } finally {
      tables.refresh()
      sendingRef.current = false
      setSending(false)
    }
  }

  function nameOf(sellableItemId: string): string {
    return (readyMenu && findItem(readyMenu, sellableItemId)?.name) ?? sellableItemId
  }

  /** Applies a basket change and returns the resulting item count. */
  function changeBasket(change: Parameters<typeof draft.update>[0]): number {
    let count = 0
    draft.update((current) => {
      const next = change(current)
      count = countItems(next)
      return next
    }, readyMenu?.masterDataVersion ?? null)
    return count
  }

  function chooseItem(item: MenuItem) {
    if (!currentTable) return
    if (item.optionGroups.length > 0) {
      navigate({ screen: 'item', table: currentTable, sellableItemId: item.sellableItemId, lineId: null })
      return
    }
    const line: DraftLine = {
      lineId: newLineId(),
      sellableItemId: item.sellableItemId,
      quantity: 1,
      selections: [],
      comment: '',
    }
    const count = changeBasket((current) => addLine(current, line))
    setMessage(text.menuAdded(item.name, count))
  }

  function saveItem(lineId: string | null, values: Omit<DraftLine, 'lineId'>) {
    const count = changeBasket((current) =>
      lineId ? replaceLine(current, { ...values, lineId, fulfillmentType: current.lines.find(line => line.lineId === lineId)?.fulfillmentType }) : addLine(current, { ...values, lineId: newLineId() }),
    )
    setMessage(lineId ? text.basketUpdated(nameOf(values.sellableItemId)) : text.menuAdded(nameOf(values.sellableItemId), count))
    back()
  }

  return (
    <>
      {route.screen === 'tables' && (
        <ActiveTablesScreen
          key="tables"
          employee={employee}
          tables={tables}
          opening={opening}
          openFailed={openFailed}
          onOpenAvailable={services.tables.openTable ? openAvailable : undefined}
          onOpenTable={(table) => navigate({ screen: 'table', table })}
        />
      )}
      {route.screen === 'table' && (
        <TableScreen
          key={`table:${route.table.tableSessionId}`}
          table={route.table}
          detail={services.tables.loadDetail && tables.snapshot?.tables.some(row => row.tableSessionId === route.table.tableSessionId) && (
            <TableDetailPanel
              gateway={services.tables}
              device={device}
              employee={employee}
              sessionId={route.table.tableSessionId}
              snapshot={tables.snapshot}
              onRequestPayment={services.outbox?.queuePayment && services.submitOrder ? requestPayment : undefined}
              onCancellation={services.outbox?.queueCancellation && services.submitOrder ? requestCancellation : undefined}
              cancellationBlocked={sending || tables.loading || !!tables.problem}
              paymentBlocked={sending || !draft.loaded || draft.saving || draft.saveFailed || !!draft.draft?.lines.length || tables.loading || !!tables.problem}
            />
          )}
          tables={tables}
          draft={draft}
          sending={sending}
          onSend={services.outbox && services.submitOrder ? sendOrder : undefined}
          persistence={services.drafts.persistence}
          menu={readyMenu}
          message={message}
          onAddItems={() => navigate({ screen: 'menu', table: route.table })}
          onQuantity={(line, quantity) => {
            changeBasket((current) => setLineQuantity(current, line.lineId, quantity))
          }}
          onFulfillment={(line, fulfillmentType) => {
            changeBasket(current => {
              const latest = current.lines.find(candidate => candidate.lineId === line.lineId)
              return latest ? replaceLine(current, { ...latest, fulfillmentType }) : current
            })
          }}
          onEdit={(line) =>
            navigate({
              screen: 'item',
              table: route.table,
              sellableItemId: line.sellableItemId,
              lineId: line.lineId,
            })
          }
          onRemove={(line) => {
            changeBasket((current) => removeLine(current, line.lineId))
            setMessage(text.basketRemoved(nameOf(line.sellableItemId)))
          }}
        />
      )}
      {route.screen === 'menu' && (
        <MenuScreen
          key={`menu:${route.table.tableSessionId}`}
          table={route.table}
          menu={menu}
          draft={draft.draft}
          message={message}
          onChooseItem={chooseItem}
          onViewBasket={() => navigate({ screen: 'table', table: route.table })}
        />
      )}
      {route.screen === 'item' && (
        <ItemRoute
          key={`item:${route.table.tableSessionId}:${route.sellableItemId}:${route.lineId ?? 'new'}`}
          item={readyMenu ? findItem(readyMenu, route.sellableItemId) : undefined}
          loading={menu.state.status === 'loading' || !draft.loaded}
          line={route.lineId ? (draft.draft?.lines.find((line) => line.lineId === route.lineId) ?? null) : null}
          editing={route.lineId !== null}
          onSave={(values) => saveItem(route.lineId, values)}
        />
      )}
      {route.screen === 'status' && (
        <StatusScreen key="status" device={device} employee={employee} source={services.syncStatus} soldOut={services.soldOut} />
      )}
      <BottomNav route={route} currentTable={currentTable} onNavigate={navigate} />
    </>
  )
}

interface ItemRouteProps {
  readonly item: MenuItem | undefined
  readonly loading: boolean
  readonly line: DraftLine | null
  readonly editing: boolean
  readonly onSave: (values: Omit<DraftLine, 'lineId'>) => void
}

/** Shows the options screen once the menu and basket are available, or explains why it cannot. */
function ItemRoute({ item, loading, line, editing, onSave }: ItemRouteProps) {
  if (loading) {
    return (
      <Screen title={text.menuLoading} busy>
        <p className="muted">{text.menuLoading}</p>
      </Screen>
    )
  }
  if (!item || (editing && !line)) {
    return (
      <Screen title={text.itemUnavailableTitle}>
        <Notice>
          <p>{!item ? text.menuItemMissing : text.itemLineMissing}</p>
        </Notice>
      </Screen>
    )
  }
  return <ItemScreen item={item} line={line} onSave={onSave} />
}
