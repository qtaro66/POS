import { describe, expect, it } from 'vitest'
import { FIXTURE_EMPLOYEE } from '../adapters/fixtures.ts'
import { isSessionExpired, sessionExpiresAtMs } from './sessionExpiry.ts'

const EXPIRES_AT = '2026-01-15T11:00:00Z'
const employee = { ...FIXTURE_EMPLOYEE, sessionId: 'test-session-id', expiresAt: EXPIRES_AT }

describe('sessionExpiresAtMs', () => {
  it('uses the Branch-provided expiresAt', () => {
    expect(sessionExpiresAtMs(employee)).toBe(Date.parse(EXPIRES_AT))
  })

  it('has no expiry when expiresAt is absent (fixture sessions)', () => {
    expect(FIXTURE_EMPLOYEE.expiresAt).toBeUndefined()
    expect(sessionExpiresAtMs(FIXTURE_EMPLOYEE)).toBeNull()
  })
})

describe('isSessionExpired', () => {
  it('is usable until the expiry instant and expired from it on', () => {
    expect(isSessionExpired(employee, new Date('2026-01-15T10:59:59.999Z'))).toBe(false)
    expect(isSessionExpired(employee, new Date(EXPIRES_AT))).toBe(true)
    expect(isSessionExpired(employee, new Date('2026-01-16T00:00:00Z'))).toBe(true)
  })

  it('never expires a session without expiresAt', () => {
    expect(isSessionExpired(FIXTURE_EMPLOYEE, new Date('2099-01-01T00:00:00Z'))).toBe(false)
  })

  it('treats an unreadable expiresAt as expired', () => {
    expect(isSessionExpired({ ...employee, expiresAt: 'soon' }, new Date(0))).toBe(true)
  })
})
