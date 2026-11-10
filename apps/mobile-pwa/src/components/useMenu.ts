import { useCallback, useEffect, useMemo, useState } from 'react'
import type { MenuGateway } from '../adapters/ports.ts'
import type { Menu } from '../domain/menu.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'

export type MenuState =
  | { readonly status: 'loading' }
  | { readonly status: 'ready'; readonly menu: Menu }
  | { readonly status: 'unreachable' }
  | { readonly status: 'failed' }

export interface MenuData {
  readonly state: MenuState
  readonly retry: () => void
}

const LOADING: MenuState = { status: 'loading' }

/**
 * Loads the menu of one table session. It is loaded again when the table
 * session changes or on retry, so sellability (Sold Out) is as current as the
 * last load; the Branch Service still checks it on submit (D09).
 */
export function useMenu(
  gateway: MenuGateway,
  device: PairedDevice,
  tableSessionId: string | null,
  employee: EmployeeSession,
): MenuData {
  const [attempt, setAttempt] = useState(0)
  const request = useMemo(() => ({ gateway, device, employee, tableSessionId, attempt }),
    [gateway, device, employee, tableSessionId, attempt])
  const [result, setResult] = useState<{ readonly request: typeof request; readonly state: MenuState }>()

  useEffect(() => {
    if (!tableSessionId) return
    const controller = new AbortController()
    void gateway.loadMenu(device, tableSessionId, employee, controller.signal).then(
      (outcome) => {
        if (controller.signal.aborted) return
        setResult({
          request,
          state: outcome.kind === 'ok' ? { status: 'ready', menu: outcome.menu } : { status: 'unreachable' },
        })
      },
      () => {
        if (!controller.signal.aborted) setResult({ request, state: { status: 'failed' } })
      },
    )
    return () => controller.abort()
  }, [device, employee, gateway, request, tableSessionId])

  const retry = useCallback(() => setAttempt((count) => count + 1), [])

  return { state: result && result.request === request ? result.state : LOADING, retry }
}
