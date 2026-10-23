/**
 * Screens of the authenticated Mobile shell. The current table is identified
 * by its `tableSessionId`; its label is kept so the navigation can name it
 * even when the table is missing from the latest Branch list.
 */
export interface CurrentTable {
  readonly tableSessionId: string
  readonly tableLabel: string
}

export type ShellRoute =
  | { readonly screen: 'tables' }
  | { readonly screen: 'table'; readonly table: CurrentTable }
  | { readonly screen: 'status' }

export const TABLES_ROUTE: ShellRoute = { screen: 'tables' }

/** Reads a route stored in `history.state`; anything else is not a shell route. */
export function readShellRoute(value: unknown): ShellRoute | null {
  if (typeof value !== 'object' || value === null) return null
  const route = value as Record<string, unknown>
  if (route.screen === 'tables' || route.screen === 'status') {
    return { screen: route.screen }
  }
  if (route.screen === 'table' && typeof route.table === 'object' && route.table !== null) {
    const table = route.table as Record<string, unknown>
    if (typeof table.tableSessionId === 'string' && typeof table.tableLabel === 'string') {
      return {
        screen: 'table',
        table: { tableSessionId: table.tableSessionId, tableLabel: table.tableLabel },
      }
    }
  }
  return null
}

export function isSameRoute(a: ShellRoute, b: ShellRoute): boolean {
  if (a.screen === 'table' && b.screen === 'table') {
    return a.table.tableSessionId === b.table.tableSessionId
  }
  return a.screen === b.screen
}
