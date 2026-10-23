import { useCallback, useEffect, useRef, useState } from 'react'
import {
  isSameRoute,
  isTableScreen,
  readShellRoute,
  TABLES_ROUTE,
  type CurrentTable,
  type ShellRoute,
} from '../domain/shellRoute.ts'

/** Key under which the shell keeps its route in `history.state`. */
const HISTORY_KEY = 'posMobileShell'

function routeFromHistory(state: unknown): ShellRoute | null {
  if (typeof state !== 'object' || state === null) return null
  return readShellRoute((state as Record<string, unknown>)[HISTORY_KEY])
}

export interface ShellNavigation {
  readonly route: ShellRoute
  /** Table opened most recently in this login, kept while other screens are shown. */
  readonly currentTable: CurrentTable | null
  readonly navigate: (route: ShellRoute) => void
  /** Returns to the previous screen, as the system back action does. */
  readonly back: () => void
}

/**
 * Route state of the authenticated shell, kept in the browser history so the
 * Android back gesture and the browser back button return to the previous
 * screen. The URL never changes: a reload always starts at the PIN login, so
 * no screen can be opened directly from a link.
 */
export function useShellNavigation(
  /** Called on every screen change, including the system back action. */
  onShow?: (route: ShellRoute) => void,
): ShellNavigation {
  const [route, setRoute] = useState<ShellRoute>(TABLES_ROUTE)
  const [currentTable, setCurrentTable] = useState<CurrentTable | null>(null)
  // Mirrors `route` so `navigate` can compare without a state updater with side effects.
  const routeRef = useRef<ShellRoute>(TABLES_ROUTE)
  const onShowRef = useRef(onShow)
  useEffect(() => {
    onShowRef.current = onShow
  })

  const show = useCallback((next: ShellRoute) => {
    routeRef.current = next
    setRoute(next)
    if (isTableScreen(next)) setCurrentTable(next.table)
    onShowRef.current?.(next)
  }, [])

  useEffect(() => {
    window.history.replaceState({ [HISTORY_KEY]: TABLES_ROUTE }, '')
    const onPopState = (event: PopStateEvent) => {
      show(routeFromHistory(event.state) ?? TABLES_ROUTE)
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [show])

  const navigate = useCallback(
    (next: ShellRoute) => {
      if (isSameRoute(routeRef.current, next)) return
      window.history.pushState({ [HISTORY_KEY]: next }, '')
      show(next)
    },
    [show],
  )

  const back = useCallback(() => window.history.back(), [])

  return { route, currentTable, navigate, back }
}
