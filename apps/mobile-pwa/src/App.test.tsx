import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import {
  createFixtureServices,
  FIXTURE_ACCEPTED_PIN,
  FIXTURE_DEVICE,
  FIXTURE_PAIRING_PAYLOAD,
  FIXTURE_PAIRING_QR_TEXT,
  type FixtureOptions,
} from './adapters/fixtures.ts'
import type { MobileServices } from './adapters/ports.ts'
import type { PairingQrPayload } from './domain/types.ts'

function renderApp(options: FixtureOptions = {}) {
  const services = createFixtureServices(options)
  const user = userEvent.setup()
  render(<App services={services} initialDevice={options.pairedDevice ?? null} />)
  return { services, user }
}

/** Renders the app with one adapter replaced, for example by one whose promise rejects. */
function renderWith(options: FixtureOptions, override: Partial<MobileServices>) {
  const services = { ...createFixtureServices(options), ...override }
  const user = userEvent.setup()
  render(<App services={services} initialDevice={options.pairedDevice ?? null} />)
  return { services, user }
}

const unexpected = () => Promise.reject(new Error('fixture: unexpected adapter failure'))

async function startPairing(user: ReturnType<typeof userEvent.setup>, name = 'Fixture handheld 1') {
  const field = screen.getByLabelText('Device name') as HTMLInputElement
  await user.clear(field)
  if (name) await user.type(field, name)
  await user.click(screen.getByRole('button', { name: 'Scan pairing QR code' }))
}

async function logIn(user: ReturnType<typeof userEvent.setup>, pin = FIXTURE_ACCEPTED_PIN) {
  await user.type(screen.getByLabelText('PIN'), pin)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
}

describe('unpaired device', () => {
  it('shows only the pairing entry and no sync status', () => {
    renderApp()
    expect(screen.getByRole('heading', { level: 1, name: 'Pair this device' })).toBeTruthy()
    expect(screen.queryByRole('region', { name: 'Connection and sync status' })).toBeNull()
    expect(screen.queryByLabelText('PIN')).toBeNull()
  })

  it('moves focus to the screen heading', () => {
    renderApp()
    expect(document.activeElement).toBe(screen.getByRole('heading', { level: 1 }))
  })

  it('pairs from a scanned QR code, stores the device and asks for a PIN', async () => {
    const { services, user } = renderApp()
    await startPairing(user)

    expect(await screen.findByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.getByText(/Fixture handheld 1 · Fixture Branch 001/)).toBeTruthy()
    expect(await services.deviceStore.load()).toEqual(FIXTURE_DEVICE)
  })

  it.each([
    [{ kind: 'rejected', reason: 'expired' } as const, 'This pairing code has expired.'],
    [{ kind: 'rejected', reason: 'alreadyUsed' } as const, 'already been used'],
    [{ kind: 'rejected', reason: 'invalid' } as const, 'may have expired or already been used'],
    [{ kind: 'rejected', reason: 'invalidRequest' } as const, 'Check the device name'],
    [{ kind: 'rejected', reason: 'rateLimited' } as const, 'Too many pairing attempts'],
    [{ kind: 'unreachable' } as const, 'branch service cannot be reached'],
  ])('stays unpaired when the branch answers %o', async (pairingOutcome, message) => {
    const { services, user } = renderApp({ pairingOutcome })
    await startPairing(user)

    expect((await screen.findByRole('alert')).textContent).toContain(message)
    expect(screen.getByRole('heading', { level: 1, name: 'Pair this device' })).toBeTruthy()
    expect(await services.deviceStore.load()).toBeNull()
  })

  it.each([
    [FIXTURE_PAIRING_QR_TEXT.replace('https://', 'http://'), 'HTTPS'],
    [FIXTURE_PAIRING_QR_TEXT.replace('pair?v=1&', 'pair?v=2&'), 'different POS version'],
    ['not a pairing code', 'not a valid POS pairing code'],
  ])('refuses scanned text %j before contacting the branch', async (scanned, message) => {
    let pairCalls = 0
    const services = createFixtureServices({ scanResult: { kind: 'scanned', text: scanned } })
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...services,
          pairing: {
            pair: (payload, name) => {
              pairCalls += 1
              return services.pairing.pair(payload, name)
            },
          },
        }}
        initialDevice={null}
      />,
    )
    await startPairing(user)
    expect((await screen.findByRole('alert')).textContent).toContain(message)
    expect(pairCalls).toBe(0)
  })

  it('sends the parsed contract fields to the pairing gateway', async () => {
    const received: [PairingQrPayload, string][] = []
    const services = createFixtureServices()
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...services,
          pairing: {
            pair: (payload, name) => {
              received.push([payload, name])
              return services.pairing.pair(payload, name)
            },
          },
        }}
        initialDevice={null}
      />,
    )
    await startPairing(user)
    await screen.findByRole('heading', { level: 1, name: 'Employee login' })
    expect(received).toEqual([[FIXTURE_PAIRING_PAYLOAD, 'Fixture handheld 1']])
  })

  it('requires a device name before scanning', async () => {
    let scans = 0
    const { user } = renderWith(
      {},
      {
        qrScanner: {
          scan: () => {
            scans += 1
            return Promise.resolve({ kind: 'cancelled' } as const)
          },
        },
      },
    )
    await startPairing(user, '   ')

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toBe('Enter a name for this device.')
    const field = screen.getByLabelText('Device name')
    expect(field.getAttribute('aria-invalid')).toBe('true')
    expect(field.getAttribute('aria-describedby')).toBe(alert.id)
    expect(document.activeElement).toBe(field)
    expect(scans).toBe(0)
  })

  it('sends the trimmed device name', async () => {
    const { services, user } = renderApp()
    await startPairing(user, '  Front handheld  ')
    await screen.findByRole('heading', { level: 1, name: 'Employee login' })
    expect((await services.deviceStore.load())?.deviceName).toBe('Front handheld')
  })

  it('reports a cancelled scan', async () => {
    const { user } = renderApp({ scanResult: { kind: 'cancelled' } })
    await startPairing(user)
    expect((await screen.findByRole('alert')).textContent).toContain('cancelled')
  })
})

describe('paired device, logged out', () => {
  it('starts on the PIN login when a paired device is stored', () => {
    renderApp({ pairedDevice: FIXTURE_DEVICE })
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.getByRole('region', { name: 'Connection and sync status' })).toBeTruthy()
  })

  it('uses a masked numeric PIN field', () => {
    renderApp({ pairedDevice: FIXTURE_DEVICE })
    const pin = screen.getByLabelText('PIN')
    expect(pin.getAttribute('type')).toBe('password')
    expect(pin.getAttribute('inputmode')).toBe('numeric')
    expect(pin.getAttribute('autocomplete')).toBe('off')
  })

  it('asks for a PIN when the form is submitted empty', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await user.click(screen.getByRole('button', { name: 'Log in' }))
    expect(screen.getByRole('alert').textContent).toBe('Enter your PIN.')
    expect(document.activeElement).toBe(screen.getByLabelText('PIN'))
  })

  it('rejects a wrong PIN, clears the field and keeps focus on it', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await logIn(user, '1111')

    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('not accepted')
    const pin = screen.getByLabelText('PIN') as HTMLInputElement
    expect(pin.value).toBe('')
    expect(pin.getAttribute('aria-invalid')).toBe('true')
    expect(pin.getAttribute('aria-describedby')).toBe(alert.id)
    expect(document.activeElement).toBe(pin)
  })

  it('ignores non-digit characters typed into the PIN field', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await user.type(screen.getByLabelText('PIN'), '2a4b6c8')
    expect((screen.getByLabelText('PIN') as HTMLInputElement).value).toBe('2468')
  })

  it('submits the PIN with the keyboard Enter key', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await user.type(screen.getByLabelText('PIN'), `${FIXTURE_ACCEPTED_PIN}{Enter}`)
    expect(await screen.findByRole('heading', { level: 1, name: 'Active tables' })).toBeTruthy()
  })

  it('explains that the PIN cannot be checked while the branch is offline', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE, connection: 'offline' })
    await logIn(user)
    expect((await screen.findByRole('alert')).textContent).toContain('cannot be checked')
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
  })

  it('shows how many unsent items are kept on the device', () => {
    renderApp({
      pairedDevice: FIXTURE_DEVICE,
      outbox: { draft: 2, pending: 1, submitted: 0, unknown: 0 },
    })
    expect(screen.getByText('3 unsent items are saved on this device.')).toBeTruthy()
  })
})

describe('employee authenticated', () => {
  it('lists every active table with its Branch-reported status', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await logIn(user)

    const list = await screen.findByRole('list', { name: 'Active tables' })
    const items = within(list).getAllByRole('listitem')
    expect(items).toHaveLength(3)
    expect(within(items[0]).getByRole('heading', { name: 'Table A1' })).toBeTruthy()
    expect(within(items[0]).getByText('Ordering')).toBeTruthy()
    expect(within(items[1]).getByText('Payment pending')).toBeTruthy()
    expect(within(items[2]).getByText('Partially paid')).toBeTruthy()
    expect(screen.getByText('Signed in as Fixture Employee A')).toBeTruthy()
  })

  it('shows an empty state when no table is open', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE, tables: [] })
    await logIn(user)
    expect(await screen.findByText('No tables are open at this branch.')).toBeTruthy()
  })

  it('reports an unreachable branch when loading tables fails', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE, tablesUnreachable: true })
    await logIn(user)
    expect((await screen.findByRole('alert')).textContent).toContain('could not be refreshed')
    expect(screen.queryByRole('list', { name: 'Active tables' })).toBeNull()
  })

  it('keeps the last list and warns when a refresh fails after going offline', async () => {
    const { services, user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    act(() => services.syncStatus.update({ connection: 'offline' }))
    expect(screen.getByRole('status').textContent).toContain('Branch offline')
    expect(screen.getByText(/Orders cannot be sent while the branch is offline/)).toBeTruthy()

    await user.click(screen.getByRole('button', { name: 'Refresh tables' }))
    expect((await screen.findByRole('alert')).textContent).toContain(
      'Showing the last list received from the branch.',
    )
    expect(screen.getByRole('list', { name: 'Active tables' })).toBeTruthy()
  })

  it('shows online status, employee-data sync time and the outbox count', async () => {
    const { user } = renderApp({
      pairedDevice: FIXTURE_DEVICE,
      outbox: { draft: 0, pending: 1, submitted: 0, unknown: 1 },
    })
    await logIn(user)
    const status = screen.getByRole('status')
    expect(status.textContent).toContain('Branch online')
    expect(status.textContent).toContain('Employee data synced')
    expect(status.querySelector('time')?.getAttribute('dateTime')).toBe(
      '2026-01-15T02:58:00.000Z',
    )
    expect(status.textContent).toContain('2 unsent items')
  })

  it('reports when employee data has never been synced', () => {
    renderApp({ pairedDevice: FIXTURE_DEVICE, employeeDataSyncedAt: null })
    expect(screen.getByRole('status').textContent).toContain('Employee data not yet synced')
  })

  it('logs out to the PIN screen, keeps the pairing and shows unsent items', async () => {
    const { services, user } = renderApp({
      pairedDevice: FIXTURE_DEVICE,
      outbox: { draft: 1, pending: 0, submitted: 0, unknown: 0 },
    })
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    await user.click(screen.getByRole('button', { name: 'Log out' }))
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.getByText('1 unsent item is saved on this device.')).toBeTruthy()
    expect(await services.deviceStore.load()).toEqual(FIXTURE_DEVICE)
  })
})

describe('recovery from unexpected adapter failures', () => {
  it('recovers when the QR scanner promise rejects', async () => {
    const { user } = renderWith({}, { qrScanner: { scan: unexpected } })
    await startPairing(user)

    expect((await screen.findByRole('alert')).textContent).toContain('unexpected error')
    const scan = screen.getByRole('button', { name: 'Scan pairing QR code' })
    expect((scan as HTMLButtonElement).disabled).toBe(false)
  })

  it('recovers when the pairing gateway promise rejects and can pair on retry', async () => {
    const services = createFixtureServices()
    let calls = 0
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...services,
          pairing: {
            pair: (payload, name) => {
              calls += 1
              return calls === 1 ? unexpected() : services.pairing.pair(payload, name)
            },
          },
        }}
        initialDevice={null}
      />,
    )
    await startPairing(user)
    expect((await screen.findByRole('alert')).textContent).toContain('unexpected error')
    expect(await services.deviceStore.load()).toBeNull()

    await startPairing(user)
    expect(await screen.findByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
  })

  it('clears the PIN and re-enables the form when the login promise rejects', async () => {
    const services = createFixtureServices({ pairedDevice: FIXTURE_DEVICE })
    let calls = 0
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...services,
          auth: {
            ...services.auth,
            login: (device, pin) => {
              calls += 1
              return calls === 1 ? unexpected() : services.auth.login(device, pin)
            },
          },
        }}
        initialDevice={FIXTURE_DEVICE}
      />,
    )
    await logIn(user)

    expect((await screen.findByRole('alert')).textContent).toContain('unexpected error')
    const pin = screen.getByLabelText('PIN') as HTMLInputElement
    const submit = screen.getByRole('button', { name: 'Log in' }) as HTMLButtonElement
    expect(pin.value).toBe('')
    expect(pin.disabled).toBe(false)
    expect(submit.disabled).toBe(false)
    expect(document.activeElement).toBe(pin)

    await logIn(user)
    expect(await screen.findByRole('heading', { level: 1, name: 'Active tables' })).toBeTruthy()
  })

  it('re-enables the form after a rejected PIN too', async () => {
    const { user } = renderApp({ pairedDevice: FIXTURE_DEVICE })
    await logIn(user, '1111')
    await screen.findByRole('alert')
    expect((screen.getByLabelText('PIN') as HTMLInputElement).disabled).toBe(false)
    expect((screen.getByRole('button', { name: 'Log in' }) as HTMLButtonElement).disabled).toBe(
      false,
    )
  })

  it('recovers when the initial table load promise rejects', async () => {
    const { user } = renderWith(
      { pairedDevice: FIXTURE_DEVICE },
      { tables: { listActiveTables: unexpected } },
    )
    await logIn(user)

    expect((await screen.findByRole('alert')).textContent).toContain('unexpected error')
    const refresh = screen.getByRole('button', { name: 'Refresh tables' }) as HTMLButtonElement
    expect(refresh.disabled).toBe(false)
    expect(screen.getByRole('main').getAttribute('aria-busy')).toBe('false')
  })

  it('keeps the last list and re-enables refresh when a refresh promise rejects', async () => {
    const services = createFixtureServices({ pairedDevice: FIXTURE_DEVICE })
    let calls = 0
    const user = userEvent.setup()
    render(
      <App
        services={{
          ...services,
          tables: {
            listActiveTables: (device) => {
              calls += 1
              return calls === 1 ? services.tables.listActiveTables(device) : unexpected()
            },
          },
        }}
        initialDevice={FIXTURE_DEVICE}
      />,
    )
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    await user.click(screen.getByRole('button', { name: 'Refresh tables' }))
    const alert = await screen.findByRole('alert')
    expect(alert.textContent).toContain('unexpected error')
    expect(alert.textContent).toContain('Showing the last list received from the branch.')
    expect(within(screen.getByRole('list', { name: 'Active tables' })).getAllByRole('listitem'))
      .toHaveLength(3)
    const refresh = screen.getByRole('button', { name: 'Refresh tables' }) as HTMLButtonElement
    expect(refresh.disabled).toBe(false)
  })

  it('logs out locally even when notifying the branch rejects', async () => {
    const services = createFixtureServices({ pairedDevice: FIXTURE_DEVICE })
    const user = userEvent.setup()
    render(
      <App
        services={{ ...services, auth: { ...services.auth, logout: unexpected } }}
        initialDevice={FIXTURE_DEVICE}
      />,
    )
    await logIn(user)
    await screen.findByRole('list', { name: 'Active tables' })

    await user.click(screen.getByRole('button', { name: 'Log out' }))
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
  })
})
