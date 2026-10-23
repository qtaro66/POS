import type { MobileServices } from '../adapters/ports.ts'
import { BottomNav } from '../components/BottomNav.tsx'
import { useActiveTables } from '../components/useActiveTables.ts'
import { useShellNavigation } from '../components/useShellNavigation.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'
import { ActiveTablesScreen } from './ActiveTablesScreen.tsx'
import { StatusScreen } from './StatusScreen.tsx'
import { TableScreen } from './TableScreen.tsx'

interface AuthenticatedShellProps {
  readonly device: PairedDevice
  readonly employee: EmployeeSession
  readonly services: Pick<MobileServices, 'tables' | 'syncStatus'>
}

/**
 * Screens available to a logged-in employee, with the bottom navigation.
 * Each screen is keyed by its route so that navigating remounts it and moves
 * focus to its heading. The table list is loaded here and shared by screens.
 */
export function AuthenticatedShell({ device, employee, services }: AuthenticatedShellProps) {
  const { route, currentTable, navigate } = useShellNavigation()
  const tables = useActiveTables(device, services.tables)

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
        <TableScreen key={`table:${route.table.tableSessionId}`} table={route.table} tables={tables} />
      )}
      {route.screen === 'status' && (
        <StatusScreen key="status" device={device} employee={employee} source={services.syncStatus} />
      )}
      <BottomNav route={route} currentTable={currentTable} onNavigate={navigate} />
    </>
  )
}
