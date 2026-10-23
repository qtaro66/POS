import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import { BranchEmployeeAuthGateway, BranchPairingGateway } from './adapters/branchApi/branchGateways.ts'
import type { FetchLike } from './adapters/branchApi/http.ts'
import { createFixtureServices, FIXTURE_PAIRING_PAYLOAD, MemoryDeviceStore } from './adapters/fixtures.ts'

// Synthetic placeholders only; none of these values is a real credential.
const CREDENTIAL = 'c'.repeat(32)
const SESSION_TOKEN = 't'.repeat(32)
const PIN = '2468'
const EXPIRES_AT = '2026-01-15T11:00:00Z'
// Fixed clock before the session expiry, so the tests do not depend on today's date.
const NOW = new Date('2026-01-15T03:00:00.000Z')
const now = () => NOW

function branchFetch(options: { readonly loginStatus?: number } = {}): FetchLike {
  return (url) => {
    if (url.endsWith('/api/v1/pairing/redemptions')) {
      return Promise.resolve({
        ok: true,
        status: 200,
        json: () =>
          Promise.resolve({
            schemaVersion: 1,
            deviceId: 'test-device-id',
            deviceCredential: CREDENTIAL,
          }),
      })
    }
    const status = options.loginStatus ?? 200
    return Promise.resolve({
      ok: status === 200,
      status,
      json: () =>
        Promise.resolve({
          schemaVersion: 1,
          employeeId: 'test-employee-id',
          sessionId: 'test-session-id',
          sessionToken: SESSION_TOKEN,
          expiresAt: EXPIRES_AT,
        }),
    })
  }
}

function renderWithBranchGateways(fetch: FetchLike, clock: () => Date = now) {
  const fixtures = createFixtureServices()
  const deviceStore = new MemoryDeviceStore()
  const options = { fetch, deviceStore, timeoutMs: 1_000, now: clock }
  const auth = new BranchEmployeeAuthGateway(options)
  render(
    <App
      services={{
        ...fixtures,
        deviceStore,
        pairing: new BranchPairingGateway(options),
        auth,
      }}
      initialDevice={null}
      now={clock}
    />,
  )
  return { user: userEvent.setup(), auth }
}

async function pairAndLogIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('Device name'), 'Front handheld')
  await user.click(screen.getByRole('button', { name: 'Scan pairing QR code' }))
  await screen.findByRole('heading', { level: 1, name: 'Employee login' })
  await user.type(screen.getByLabelText('PIN'), PIN)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
}

describe('pairing and login through the Branch API adapters', () => {
  it('reaches the table list without rendering any secret', async () => {
    const { user } = renderWithBranchGateways(branchFetch())
    await pairAndLogIn(user)

    expect(await screen.findByRole('heading', { level: 1, name: 'Active tables' })).toBeTruthy()
    expect(screen.getByText('Signed in as test-employee-id')).toBeTruthy()
    const page = document.body.textContent ?? ''
    for (const secret of [CREDENTIAL, SESSION_TOKEN, FIXTURE_PAIRING_PAYLOAD.oneTimeToken, PIN]) {
      expect(page).not.toContain(secret)
    }
  })

  it('shows a rejected PIN without leaking it', async () => {
    const { user } = renderWithBranchGateways(branchFetch({ loginStatus: 401 }))
    await pairAndLogIn(user)

    expect((await screen.findByRole('alert')).textContent).toContain('not accepted')
    expect(document.body.textContent ?? '').not.toContain(PIN)
    expect((screen.getByLabelText('PIN') as HTMLInputElement).value).toBe('')
  })

  it('drops the session token and returns to the PIN login when expiresAt passes', async () => {
    let current = NOW
    const { user, auth } = renderWithBranchGateways(branchFetch(), () => current)
    await pairAndLogIn(user)
    await screen.findByRole('heading', { level: 1, name: 'Active tables' })
    expect(auth.sessionTokenFor('test-session-id')).toBe(SESSION_TOKEN)

    current = new Date(EXPIRES_AT)
    act(() => fireEvent(document, new Event('visibilitychange')))

    expect(await screen.findByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.getByText(/Your session has expired/)).toBeTruthy()
    expect(auth.sessionTokenFor('test-session-id')).toBeNull()
  })
})
