import { isTableScreen, type CurrentTable, type ShellRoute } from '../domain/shellRoute.ts'
import { text } from '../strings.ts'

interface BottomNavProps {
  readonly route: ShellRoute
  readonly currentTable: CurrentTable | null
  readonly onNavigate: (route: ShellRoute) => void
}

interface NavItem {
  readonly key: string
  readonly label: string
  readonly route: ShellRoute
}

/**
 * Phone-first main navigation, fixed above the bottom safe area. The current
 * table appears once a table has been opened. Native buttons keep it usable
 * by touch, keyboard and switch access; `aria-current` marks the shown screen.
 */
export function BottomNav({ route, currentTable, onNavigate }: BottomNavProps) {
  const items: NavItem[] = [{ key: 'tables', label: text.navTables, route: { screen: 'tables' } }]
  if (currentTable) {
    items.push({
      key: 'table',
      label: text.tableLabel(currentTable.tableLabel),
      route: { screen: 'table', table: currentTable },
    })
  }
  items.push({ key: 'status', label: text.navStatus, route: { screen: 'status' } })

  return (
    <nav className="bottom-nav" aria-label={text.navLabel}>
      <ul className="bottom-nav__list">
        {items.map((item) => {
          // The table entry stays current on the table's menu and item screens.
          const current =
            item.route.screen === 'table' ? isTableScreen(route) : item.route.screen === route.screen
          return (
            <li key={item.key} className="bottom-nav__item">
              <button
                type="button"
                className="bottom-nav__button"
                aria-current={current ? 'page' : undefined}
                onClick={() => onNavigate(item.route)}
              >
                {item.label}
              </button>
            </li>
          )
        })}
      </ul>
    </nav>
  )
}
