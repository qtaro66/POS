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
  /** Menu for adding items to the current table's basket. */
  | { readonly screen: 'menu'; readonly table: CurrentTable }
  /** Options of one item; `lineId` is set when an existing basket line is edited. */
  | {
      readonly screen: 'item'
      readonly table: CurrentTable
      readonly sellableItemId: string
      readonly lineId: string | null
    }
  | { readonly screen: 'status' }

export const TABLES_ROUTE: ShellRoute = { screen: 'tables' }

/** Screens that belong to the current table, shown under its navigation entry. */
export function isTableScreen(route: ShellRoute): route is Extract<ShellRoute, { table: CurrentTable }> {
  return route.screen === 'table' || route.screen === 'menu' || route.screen === 'item'
}

function readTable(value: unknown): CurrentTable | null {
  if (typeof value !== 'object' || value === null) return null
  const table = value as Record<string, unknown>
  return typeof table.tableSessionId === 'string' && typeof table.tableLabel === 'string'
    ? { tableSessionId: table.tableSessionId, tableLabel: table.tableLabel }
    : null
}

/** Reads a route stored in `history.state`; anything else is not a shell route. */
export function readShellRoute(value: unknown): ShellRoute | null {
  if (typeof value !== 'object' || value === null) return null
  const route = value as Record<string, unknown>
  if (route.screen === 'tables' || route.screen === 'status') {
    return { screen: route.screen }
  }
  const table = readTable(route.table)
  if (!table) return null
  if (route.screen === 'table' || route.screen === 'menu') {
    return { screen: route.screen, table }
  }
  if (
    route.screen === 'item' &&
    typeof route.sellableItemId === 'string' &&
    (route.lineId === null || typeof route.lineId === 'string')
  ) {
    return { screen: 'item', table, sellableItemId: route.sellableItemId, lineId: route.lineId }
  }
  return null
}

export function isSameRoute(a: ShellRoute, b: ShellRoute): boolean {
  if (a.screen !== b.screen) return false
  if (a.screen === 'item' && b.screen === 'item') {
    return (
      a.table.tableSessionId === b.table.tableSessionId &&
      a.sellableItemId === b.sellableItemId &&
      a.lineId === b.lineId
    )
  }
  if (isTableScreen(a) && isTableScreen(b)) {
    return a.table.tableSessionId === b.table.tableSessionId
  }
  return true
}
