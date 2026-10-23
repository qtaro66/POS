import { describe, expect, it } from 'vitest'
import { FIXTURE_DEVICE, FIXTURE_EMPLOYEE } from '../adapters/fixtures.ts'
import { deviceSessionReducer, initialDeviceState } from './deviceSession.ts'
import { countUnsent } from './outbox.ts'
import type { DeviceState } from './types.ts'

const unpaired: DeviceState = { status: 'unpaired' }
const pairedLoggedOut: DeviceState = { status: 'pairedLoggedOut', device: FIXTURE_DEVICE }
const authenticated: DeviceState = {
  status: 'authenticated',
  device: FIXTURE_DEVICE,
  employee: FIXTURE_EMPLOYEE,
}

describe('initialDeviceState', () => {
  it('starts unpaired without a stored device', () => {
    expect(initialDeviceState(null)).toEqual(unpaired)
  })

  it('starts logged out when a paired device is stored', () => {
    expect(initialDeviceState(FIXTURE_DEVICE)).toEqual(pairedLoggedOut)
  })
})

describe('deviceSessionReducer', () => {
  it('moves through pairing, login and logout', () => {
    const paired = deviceSessionReducer(unpaired, { type: 'paired', device: FIXTURE_DEVICE })
    expect(paired).toEqual(pairedLoggedOut)
    const loggedIn = deviceSessionReducer(paired, {
      type: 'authenticated',
      employee: FIXTURE_EMPLOYEE,
    })
    expect(loggedIn).toEqual(authenticated)
    expect(deviceSessionReducer(loggedIn, { type: 'loggedOut' })).toEqual(pairedLoggedOut)
  })

  it('does not authenticate an employee on an unpaired device', () => {
    expect(
      deviceSessionReducer(unpaired, { type: 'authenticated', employee: FIXTURE_EMPLOYEE }),
    ).toBe(unpaired)
  })

  it('ignores pairing when the device is already paired', () => {
    expect(deviceSessionReducer(authenticated, { type: 'paired', device: FIXTURE_DEVICE })).toBe(
      authenticated,
    )
  })

  it('ignores logout when no employee is authenticated', () => {
    expect(deviceSessionReducer(pairedLoggedOut, { type: 'loggedOut' })).toBe(pairedLoggedOut)
  })

  it('returns an expired session to the PIN login, keeps the device and records why', () => {
    const expired = deviceSessionReducer(authenticated, { type: 'sessionExpired' })
    expect(expired).toEqual({ ...pairedLoggedOut, reason: 'sessionExpired' })
    // Logging in again clears the reason.
    expect(
      deviceSessionReducer(expired, { type: 'authenticated', employee: FIXTURE_EMPLOYEE }),
    ).toEqual(authenticated)
  })

  it('records an expiry reported before the login completes', () => {
    expect(deviceSessionReducer(pairedLoggedOut, { type: 'sessionExpired' })).toEqual({
      ...pairedLoggedOut,
      reason: 'sessionExpired',
    })
  })

  it('ignores session expiry on an unpaired device', () => {
    expect(deviceSessionReducer(unpaired, { type: 'sessionExpired' })).toBe(unpaired)
  })
})

describe('countUnsent', () => {
  it('counts every outstanding Mobile request state', () => {
    expect(countUnsent({ draft: 1, pending: 2, submitted: 3, unknown: 4 })).toBe(10)
  })
})
