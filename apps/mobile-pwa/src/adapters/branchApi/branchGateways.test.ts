import { describe, expect, it } from 'vitest'
import { FIXTURE_DEVICE, FIXTURE_PAIRING_PAYLOAD, MemoryDeviceStore } from '../fixtures.ts'
import { BranchEmployeeAuthGateway, BranchPairingGateway } from './branchGateways.ts'
import { branchUrl, BranchProtocolError, type FetchLike } from './http.ts'

// Synthetic placeholders only; none of these values is a real credential.
const CREDENTIAL = 'c'.repeat(32)
const SESSION_TOKEN = 't'.repeat(32)
const NOW = new Date('2026-01-15T03:00:00.000Z')

type Reply = { readonly status: number; readonly body?: unknown } | 'network' | 'hang' | 'notJson'

interface Call {
  readonly url: string
  readonly init: RequestInit
}

function fakeFetch(reply: Reply) {
  const calls: Call[] = []
  const fetch: FetchLike = (url, init) => {
    calls.push({ url, init })
    if (reply === 'network') return Promise.reject(new TypeError('Failed to fetch'))
    if (reply === 'hang') {
      return new Promise<never>((_resolve, reject) => {
        init.signal?.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')))
      })
    }
    if (reply === 'notJson') {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () => Promise.reject(new SyntaxError('Unexpected token')),
      })
    }
    return Promise.resolve({
      ok: reply.status >= 200 && reply.status < 300,
      status: reply.status,
      json: () => Promise.resolve(reply.body),
    })
  }
  return { fetch, calls }
}

function headersOf(call: Call): Record<string, string> {
  return call.init.headers as Record<string, string>
}

const redemption = { schemaVersion: 1, deviceId: 'test-device-id', deviceCredential: CREDENTIAL }

function pairingGateway(reply: Reply, timeoutMs = 1_000) {
  const { fetch, calls } = fakeFetch(reply)
  const deviceStore = new MemoryDeviceStore()
  const gateway = new BranchPairingGateway({ fetch, deviceStore, timeoutMs, now: () => NOW })
  return { gateway, calls, deviceStore }
}

describe('BranchPairingGateway', () => {
  it('posts the redemption contract to the HTTPS Branch origin', async () => {
    const { gateway, calls } = pairingGateway({ status: 200, body: redemption })
    await gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')

    expect(calls).toHaveLength(1)
    const [call] = calls
    expect(call.url).toBe('https://192.0.2.10:8443/api/v1/pairing/redemptions')
    expect(call.init.method).toBe('POST')
    expect(JSON.parse(call.init.body as string)).toEqual({
      schemaVersion: 1,
      branchId: FIXTURE_PAIRING_PAYLOAD.branchId,
      pairingId: FIXTURE_PAIRING_PAYLOAD.pairingId,
      oneTimeToken: FIXTURE_PAIRING_PAYLOAD.oneTimeToken,
      deviceDisplayName: 'Front handheld',
    })
    expect(headersOf(call)['Content-Type']).toBe('application/json')
    expect(headersOf(call).Authorization).toBeUndefined()
    expect(call.init.credentials).toBe('omit')
    expect(call.init.cache).toBe('no-store')
    expect(call.init.redirect).toBe('error')
  })

  it('stores the device and credential and returns only the public device record', async () => {
    const { gateway, deviceStore } = pairingGateway({ status: 200, body: redemption })
    const outcome = await gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')

    const device = {
      deviceId: 'test-device-id',
      deviceName: 'Front handheld',
      branchId: FIXTURE_PAIRING_PAYLOAD.branchId,
      branchName: FIXTURE_PAIRING_PAYLOAD.branchId,
      branchServiceUrl: FIXTURE_PAIRING_PAYLOAD.branchServiceUrl,
      pairedAt: NOW.toISOString(),
    }
    expect(outcome).toEqual({ kind: 'paired', device })
    expect(JSON.stringify(outcome)).not.toContain(CREDENTIAL)
    expect(await deviceStore.load()).toEqual(device)
    expect(await deviceStore.loadCredential('test-device-id')).toBe(CREDENTIAL)
  })

  it.each([
    [400, 'invalidRequest'],
    [401, 'invalid'],
    [429, 'rateLimited'],
  ] as const)('maps HTTP %i to the %s rejection without storing anything', async (status, reason) => {
    const { gateway, deviceStore } = pairingGateway({ status })
    expect(await gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')).toEqual({
      kind: 'rejected',
      reason,
    })
    expect(await deviceStore.load()).toBeNull()
  })

  it('reports a network failure as unreachable', async () => {
    const { gateway } = pairingGateway('network')
    expect(await gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')).toEqual({
      kind: 'unreachable',
    })
  })

  it('aborts after the timeout and reports unreachable', async () => {
    const { gateway, calls } = pairingGateway('hang', 5)
    expect(await gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')).toEqual({
      kind: 'unreachable',
    })
    expect(calls[0].init.signal?.aborted).toBe(true)
  })

  it.each([
    ['an unexpected status', { status: 500 }],
    ['a different schema version', { status: 200, body: { ...redemption, schemaVersion: 2 } }],
    ['a missing credential', { status: 200, body: { ...redemption, deviceCredential: '' } }],
    ['a body that is not JSON', 'notJson'],
  ] as const)('rejects %s as a protocol error and stores nothing', async (_name, reply) => {
    const { gateway, deviceStore } = pairingGateway(reply)
    await expect(gateway.pair(FIXTURE_PAIRING_PAYLOAD, 'Front handheld')).rejects.toBeInstanceOf(
      BranchProtocolError,
    )
    expect(await deviceStore.load()).toBeNull()
  })
})

const loginResponse = {
  schemaVersion: 1,
  employeeId: 'test-employee-id',
  sessionId: 'test-session-id',
  sessionToken: SESSION_TOKEN,
  expiresAt: '2026-01-15T11:00:00Z',
}

function authGateway(
  reply: Reply,
  credential: string | null = CREDENTIAL,
  now: () => Date = () => NOW,
) {
  const { fetch, calls } = fakeFetch(reply)
  const deviceStore = new MemoryDeviceStore(credential ? FIXTURE_DEVICE : null, credential ?? '')
  const gateway = new BranchEmployeeAuthGateway({ fetch, deviceStore, timeoutMs: 1_000, now })
  return { gateway, calls }
}

describe('BranchEmployeeAuthGateway', () => {
  it('posts the PIN with the device credential', async () => {
    const { gateway, calls } = authGateway({ status: 200, body: loginResponse })
    await gateway.login(FIXTURE_DEVICE, '2468')

    const [call] = calls
    expect(call.url).toBe('https://192.0.2.10:8443/api/v1/employee-sessions')
    expect(JSON.parse(call.init.body as string)).toEqual({ schemaVersion: 1, pin: '2468' })
    expect(headersOf(call).Authorization).toBe(`Device ${CREDENTIAL}`)
    expect(call.init.credentials).toBe('omit')
    expect(call.init.redirect).toBe('error')
  })

  it('returns the employee without the session token and keeps the token in memory', async () => {
    const { gateway } = authGateway({ status: 200, body: loginResponse })
    const outcome = await gateway.login(FIXTURE_DEVICE, '2468')

    expect(outcome).toEqual({
      kind: 'authenticated',
      employee: {
        employeeId: 'test-employee-id',
        displayName: 'test-employee-id',
        authenticatedAt: NOW.toISOString(),
        sessionId: 'test-session-id',
        expiresAt: '2026-01-15T11:00:00Z',
      },
    })
    expect(JSON.stringify(outcome)).not.toContain(SESSION_TOKEN)
    expect(gateway.sessionTokenFor('test-session-id')).toBe(SESSION_TOKEN)

    if (outcome.kind !== 'authenticated') throw new Error('expected authentication')
    await gateway.logout(FIXTURE_DEVICE, outcome.employee)
    expect(gateway.sessionTokenFor('test-session-id')).toBeNull()
  })

  it('stops handing out the token at the Branch-provided expiry', async () => {
    let now = NOW
    const { gateway } = authGateway({ status: 200, body: loginResponse }, CREDENTIAL, () => now)
    await gateway.login(FIXTURE_DEVICE, '2468')

    now = new Date('2026-01-15T10:59:59.999Z')
    expect(gateway.sessionTokenFor('test-session-id')).toBe(SESSION_TOKEN)
    now = new Date(loginResponse.expiresAt)
    expect(gateway.sessionTokenFor('test-session-id')).toBeNull()
    // The expired token is forgotten, so moving the clock back does not revive it.
    now = NOW
    expect(gateway.sessionTokenFor('test-session-id')).toBeNull()
  })

  it('discards an expired session token without contacting the Branch Service', async () => {
    const { gateway, calls } = authGateway({ status: 200, body: loginResponse })
    const outcome = await gateway.login(FIXTURE_DEVICE, '2468')
    if (outcome.kind !== 'authenticated') throw new Error('expected authentication')

    gateway.discardExpiredSession(outcome.employee)
    expect(gateway.sessionTokenFor('test-session-id')).toBeNull()
    expect(calls).toHaveLength(1)
  })

  it('maps HTTP 401 to a rejected login', async () => {
    const { gateway } = authGateway({ status: 401 })
    expect(await gateway.login(FIXTURE_DEVICE, '1111')).toEqual({ kind: 'rejected' })
  })

  it('reports a network failure as unreachable', async () => {
    const { gateway } = authGateway('network')
    expect(await gateway.login(FIXTURE_DEVICE, '2468')).toEqual({ kind: 'unreachable' })
  })

  it.each([
    ['HTTP 400', { status: 400 }],
    ['an invalid expiry', { status: 200, body: { ...loginResponse, expiresAt: 'soon' } }],
    ['a different schema version', { status: 200, body: { ...loginResponse, schemaVersion: 2 } }],
  ] as const)('rejects %s as a protocol error', async (_name, reply) => {
    const { gateway } = authGateway(reply)
    await expect(gateway.login(FIXTURE_DEVICE, '2468')).rejects.toBeInstanceOf(BranchProtocolError)
  })

  it('does not call the Branch Service when no credential is stored', async () => {
    const { gateway, calls } = authGateway({ status: 200, body: loginResponse }, null)
    await expect(gateway.login(FIXTURE_DEVICE, '2468')).rejects.toBeInstanceOf(BranchProtocolError)
    expect(calls).toHaveLength(0)
  })
})

describe('branchUrl', () => {
  it('resolves API paths against the service origin', () => {
    expect(branchUrl('https://192.0.2.10:8443/', 'api/v1/employee-sessions')).toBe(
      'https://192.0.2.10:8443/api/v1/employee-sessions',
    )
  })

  it('refuses a non-HTTPS origin', () => {
    expect(() => branchUrl('http://192.0.2.10/', 'api/v1/employee-sessions')).toThrow(
      BranchProtocolError,
    )
  })
})
