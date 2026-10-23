import { useEffect, useRef } from 'react'
import { sessionExpiresAtMs } from '../domain/sessionExpiry.ts'
import type { EmployeeSession } from '../domain/types.ts'

/** Largest delay `setTimeout` accepts; longer waits are split into several timers. */
const MAX_TIMER_DELAY_MS = 2_147_483_647

/**
 * Calls `onExpired` once the employee session reaches its Branch-provided
 * `expiresAt`. Browsers delay or suspend timers while the app is in the
 * background, so expiry is also rechecked when the app becomes visible or is
 * restored, and before any pointer or keyboard input reaches the screen.
 * Sessions without `expiresAt` (fixtures only) are never expired here.
 */
export function useSessionExpiry(
  employee: EmployeeSession | null,
  now: () => Date,
  onExpired: () => void,
): void {
  const onExpiredRef = useRef(onExpired)
  useEffect(() => {
    onExpiredRef.current = onExpired
  })

  useEffect(() => {
    if (!employee) return
    const expiresAtMs = sessionExpiresAtMs(employee)
    if (expiresAtMs === null) return

    let timer: ReturnType<typeof setTimeout> | undefined
    let expired = false
    const check = () => {
      if (expired) return
      clearTimeout(timer)
      const remainingMs = expiresAtMs - now().getTime()
      if (remainingMs <= 0) {
        expired = true
        onExpiredRef.current()
        return
      }
      timer = setTimeout(check, Math.min(remainingMs, MAX_TIMER_DELAY_MS))
    }

    const windowEvents = ['pageshow', 'focus'] as const
    const inputEvents = ['pointerdown', 'keydown'] as const
    check()
    document.addEventListener('visibilitychange', check)
    for (const type of windowEvents) window.addEventListener(type, check)
    for (const type of inputEvents) window.addEventListener(type, check, { capture: true })
    return () => {
      clearTimeout(timer)
      document.removeEventListener('visibilitychange', check)
      for (const type of windowEvents) window.removeEventListener(type, check)
      for (const type of inputEvents) window.removeEventListener(type, check, { capture: true })
    }
  }, [employee, now])
}
