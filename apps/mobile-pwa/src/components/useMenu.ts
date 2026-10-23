import { useCallback, useEffect, useState } from 'react'
import type { MenuGateway } from '../adapters/ports.ts'
import type { Menu } from '../domain/menu.ts'
import type { PairedDevice } from '../domain/types.ts'

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
): MenuData {
  const [attempt, setAttempt] = useState(0)
  // The result is tagged with the request it answers, so a new table session or
  // retry shows `loading` until its own answer arrives.
  const [result, setResult] = useState<{ readonly request: string; readonly state: MenuState } | null>(
    null,
  )
  const request = tableSessionId ? `${tableSessionId}#${attempt}` : null

  useEffect(() => {
    if (!tableSessionId || !request) return
    let active = true
    gateway.loadMenu(device, tableSessionId).then(
      (outcome) => {
        if (!active) return
        setResult({
          request,
          state: outcome.kind === 'ok' ? { status: 'ready', menu: outcome.menu } : { status: 'unreachable' },
        })
      },
      () => {
        if (active) setResult({ request, state: { status: 'failed' } })
      },
    )
    return () => {
      active = false
    }
  }, [device, gateway, request, tableSessionId])

  const retry = useCallback(() => setAttempt((count) => count + 1), [])

  return { state: result && result.request === request ? result.state : LOADING, retry }
}
