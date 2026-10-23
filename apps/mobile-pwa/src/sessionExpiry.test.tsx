import { act, fireEvent, render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from './App.tsx'
import {
  createFixtureServices,
  FIXTURE_ACCEPTED_PIN,
  FIXTURE_DEVICE,
  FIXTURE_EMPLOYEE,
  type FixtureOptions,
} from './adapters/fixtures.ts'
import type { EmployeeSession } from './domain/types.ts'

const LOGIN_AT = Date.parse('2026-01-15T03:05:00.000Z')
const HOUR_MS = 60 * 60 * 1000
const EXPIRY_MESSAGE = 'Your session has expired. Enter your PIN to log in again.'

/** Test clock that only moves when told to, so background timers cannot fire on their own. */
function manualClock(startMs = LOGIN_AT) {
  let current = startMs
  return {
    now: () => new Date(current),
    advance: (ms: number) => {
      current += ms
    },
  }
}

function sessionExpiringAt(expiresAtMs: number): EmployeeSession {
  return {
    ...FIXTURE_EMPLOYEE,
    sessionId: 'fixture-employee-session',
    expiresAt: new Date(expiresAtMs).toISOString(),
  }
}

function renderApp(options: FixtureOptions, now: () => Date) {
  const fixtures = createFixtureServices({ pairedDevice: FIXTURE_DEVICE, ...options })
  const discarded: EmployeeSession[] = []
  let tableLoads = 0
  const services = {
    ...fixtures,
    auth: {
      ...fixtures.auth,
      discardExpiredSession: (employee: EmployeeSession) => {
        discarded.push(employee)
      },
    },
    tables: {
      listActiveTables: (device: typeof FIXTURE_DEVICE) => {
        tableLoads += 1
        return fixtures.tables.listActiveTables(device)
      },
    },
  }
  render(<App services={services} initialDevice={FIXTURE_DEVICE} now={now} />)
  return { services, discarded, tableLoads: () => tableLoads, user: userEvent.setup() }
}

async function logIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('PIN'), FIXTURE_ACCEPTED_PIN)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
}

async function expectExpiredLogin() {
  expect(await screen.findByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
  expect(screen.getByText(EXPIRY_MESSAGE)).toBeTruthy()
  expect(screen.queryByRole('list', { name: 'Active tables' })).toBeNull()
}

describe('employee session expiry', () => {
  afterEach(() => {
    vi.useRealTimers()
  })

  it('returns to the PIN login when the session expires and keeps pairing and unsent items', async () => {
    // Fake timers drive both the expiry timer and the clock, so no real time passes.
    vi.useFakeTimers({ now: LOGIN_AT, toFake: ['setTimeout', 'clearTimeout', 'Date'] })
    const employee = sessionExpiringAt(LOGIN_AT + HOUR_MS)
    const fixtures = createFixtureServices({
      pairedDevice: FIXTURE_DEVICE,
      employee,
      outbox: { draft: 2, pending: 1, submitted: 0, unknown: 0 },
    })
    const discarded: EmployeeSession[] = []
    const services = {
      ...fixtures,
      auth: {
        ...fixtures.auth,
        discardExpiredSession: (session: EmployeeSession) => {
          discarded.push(session)
        },
      },
    }
    render(<App services={services} initialDevice={FIXTURE_DEVICE} now={() => new Date()} />)

    // fireEvent instead of user-event, which waits on timers between steps.
    fireEvent.change(screen.getByLabelText('PIN'), { target: { value: FIXTURE_ACCEPTED_PIN } })
    await act(async () => {
      fireEvent.click(screen.getByRole('button', { name: 'Log in' }))
    })
    expect(screen.getByRole('list', { name: 'Active tables' })).toBeTruthy()

    act(() => vi.advanceTimersByTime(HOUR_MS - 1))
    expect(screen.getByRole('list', { name: 'Active tables' })).toBeTruthy()
    expect(discarded).toEqual([])

    act(() => vi.advanceTimersByTime(1))
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.getByText(EXPIRY_MESSAGE)).toBeTruthy()
    expect(screen.queryByRole('list', { name: 'Active tables' })).toBeNull()
    expect(discarded).toEqual([employee])
    expect((screen.getByLabelText('PIN') as HTMLInputElement).value).toBe('')
    expect(await services.deviceStore.load()).toEqual(FIXTURE_DEVICE)
    expect(services.syncStatus.getStatus().outbox).toEqual({
      draft: 2,
      pending: 1,
      submitted: 0,
      unknown: 0,
    })
    expect(screen.getByText('3 unsent items are saved on this device.')).toBeTruthy()
  })

  it.each([
    ['becomes visible', () => fireEvent(document, new Event('visibilitychange'))],
    ['is restored from the page cache', () => fireEvent(window, new Event('pageshow'))],
    ['regains focus', () => fireEvent(window, new Event('focus'))],
  ])('rechecks expiry when the app %s after its timer was delayed', async (_name, resume) => {
    const clock = manualClock()
    const employee = sessionExpiringAt(LOGIN_AT + HOUR_MS)
    const { discarded, user } = renderApp({ employee }, clock.now)
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    // The device slept past the expiry; the one-hour timer has not fired.
    clock.advance(2 * HOUR_MS)
    expect(screen.getByRole('heading', { level: 1, name: 'Active tables' })).toBeTruthy()

    act(resume)
    await expectExpiredLogin()
    expect(discarded).toEqual([employee])
  })

  it('does not let input reach the screen once the session has expired', async () => {
    const clock = manualClock()
    const { tableLoads, user } = renderApp(
      { employee: sessionExpiringAt(LOGIN_AT + HOUR_MS) },
      clock.now,
    )
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })
    expect(tableLoads()).toBe(1)

    clock.advance(HOUR_MS)
    await user.click(screen.getByRole('button', { name: 'Refresh tables' }))
    await expectExpiredLogin()
    expect(tableLoads()).toBe(1)
  })

  it('never opens a session that is already expired when the login returns', async () => {
    const clock = manualClock()
    const employee = sessionExpiringAt(LOGIN_AT - 1)
    const { discarded, tableLoads, user } = renderApp({ employee }, clock.now)
    await logIn(user)

    expect(await screen.findByText(EXPIRY_MESSAGE)).toBeTruthy()
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(discarded).toEqual([employee])
    expect(tableLoads()).toBe(0)
  })

  it('logs in again after expiry with a new session and clears the message', async () => {
    const clock = manualClock()
    const fixtures = createFixtureServices({ pairedDevice: FIXTURE_DEVICE })
    let logins = 0
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...fixtures,
          auth: {
            ...fixtures.auth,
            login: async (device, pin) => {
              const outcome = await fixtures.auth.login(device, pin)
              if (outcome.kind !== 'authenticated') return outcome
              logins += 1
              return {
                kind: 'authenticated',
                employee: {
                  ...outcome.employee,
                  sessionId: `fixture-employee-session-${logins}`,
                  expiresAt: new Date(clock.now().getTime() + HOUR_MS).toISOString(),
                },
              }
            },
          },
        }}
        initialDevice={FIXTURE_DEVICE}
        now={clock.now}
      />,
    )
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    clock.advance(HOUR_MS)
    act(() => fireEvent(document, new Event('visibilitychange')))
    await expectExpiredLogin()

    await logIn(user)
    expect(await screen.findByRole('list', { name: 'Active tables' })).toBeTruthy()
    expect(screen.queryByText(EXPIRY_MESSAGE)).toBeNull()
    expect(logins).toBe(2)

    // The new session is checked against its own expiry, not the old one.
    clock.advance(HOUR_MS - 1)
    act(() => fireEvent(document, new Event('visibilitychange')))
    expect(screen.getByRole('list', { name: 'Active tables' })).toBeTruthy()
  })

  it('keeps a fixture session without expiresAt signed in', async () => {
    const clock = manualClock()
    const { user } = renderApp({}, clock.now)
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    clock.advance(365 * 24 * HOUR_MS)
    act(() => fireEvent(document, new Event('visibilitychange')))
    expect(screen.getByRole('list', { name: 'Active tables' })).toBeTruthy()
    expect(screen.queryByText(EXPIRY_MESSAGE)).toBeNull()
  })

  it('shows no expiry message after an ordinary logout', async () => {
    const clock = manualClock()
    const { user } = renderApp({ employee: sessionExpiringAt(LOGIN_AT + HOUR_MS) }, clock.now)
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    await user.click(screen.getByRole('button', { name: 'Log out' }))
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.queryByText(EXPIRY_MESSAGE)).toBeNull()
  })
})
