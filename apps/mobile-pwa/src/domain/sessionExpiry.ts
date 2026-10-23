import type { EmployeeSession } from './types.ts'

/**
 * Expiry instant of an employee session in epoch milliseconds, taken only from
 * the Branch-provided `expiresAt`; the client never assumes a session
 * lifetime of its own.
 *
 * Returns `null` when `expiresAt` is absent. Only the fixture adapters produce
 * such sessions; they never expire on the client. The Branch adapter rejects
 * a login response without a valid `expiresAt`. A present but unreadable
 * value is treated as already expired so that it fails closed.
 */
export function sessionExpiresAtMs(employee: EmployeeSession): number | null {
  if (employee.expiresAt === undefined) return null
  const expiresAt = Date.parse(employee.expiresAt)
  return Number.isNaN(expiresAt) ? Number.NEGATIVE_INFINITY : expiresAt
}

/** Whether the session is no longer usable at `now`. The expiry instant itself counts as expired. */
export function isSessionExpired(employee: EmployeeSession, now: Date): boolean {
  const expiresAt = sessionExpiresAtMs(employee)
  return expiresAt !== null && now.getTime() >= expiresAt
}
