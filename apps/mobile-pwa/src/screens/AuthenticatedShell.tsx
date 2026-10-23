import { useState } from 'react'
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
import { TableScreen } from './TableScreen.tsx'

interface AuthenticatedShellProps {
  readonly device: PairedDevice
  readonly employee: EmployeeSession
  readonly services: Pick<MobileServices, 'tables' | 'menu' | 'drafts' | 'syncStatus'>
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
  const [message, setMessage] = useState<string | null>(null)
  // Confirmations belong to the table's screens; leaving them clears the last one.
  const { route, currentTable, navigate, back } = useShellNavigation((next) => {
    if (next.screen === 'tables' || next.screen === 'status') setMessage(null)
  })
  const tables = useActiveTables(device, services.tables)
  const tableSessionId = currentTable?.tableSessionId ?? null
  const menu = useMenu(services.menu, device, tableSessionId)
  const readyMenu = menu.state.status === 'ready' ? menu.state.menu : null
  const draft = useDraft(
    services.drafts,
    tableSessionId
      ? { deviceId: device.deviceId, employeeId: employee.employeeId, tableSessionId }
      : null,
    now,
  )

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
      lineId ? replaceLine(current, { ...values, lineId }) : addLine(current, { ...values, lineId: newLineId() }),
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
          onOpenTable={(table) => navigate({ screen: 'table', table })}
        />
      )}
      {route.screen === 'table' && (
        <TableScreen
          key={`table:${route.table.tableSessionId}`}
          table={route.table}
          tables={tables}
          draft={draft}
          persistence={services.drafts.persistence}
          menu={readyMenu}
          message={message}
          onAddItems={() => navigate({ screen: 'menu', table: route.table })}
          onQuantity={(line, quantity) => {
            changeBasket((current) => setLineQuantity(current, line.lineId, quantity))
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
        <StatusScreen key="status" device={device} employee={employee} source={services.syncStatus} />
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
