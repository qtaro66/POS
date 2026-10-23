import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import {
  createFixtureServices,
  FIXTURE_ACCEPTED_PIN,
  FIXTURE_ACTIVE_TABLES,
  FIXTURE_DEVICE,
  FIXTURE_TABLES_RECEIVED_AT,
  type FixtureOptions,
} from './adapters/fixtures.ts'
import type { ActiveTable } from './domain/types.ts'

function renderShell(options: FixtureOptions = {}) {
  const fixtures = createFixtureServices({ pairedDevice: FIXTURE_DEVICE, ...options })
  let listed: readonly ActiveTable[] = options.tables ?? FIXTURE_ACTIVE_TABLES
  let tableLoads = 0
  const services = {
    ...fixtures,
    tables: {
      listActiveTables: () => {
        tableLoads += 1
        return Promise.resolve({
          kind: 'ok' as const,
          snapshot: { tables: listed, receivedAt: FIXTURE_TABLES_RECEIVED_AT },
        })
      },
    },
  }
  render(<App services={services} initialDevice={FIXTURE_DEVICE} />)
  return {
    user: userEvent.setup(),
    tableLoads: () => tableLoads,
    setListed: (tables: readonly ActiveTable[]) => {
      listed = tables
    },
  }
}

async function logIn(user: ReturnType<typeof userEvent.setup>) {
  await user.type(screen.getByLabelText('PIN'), FIXTURE_ACCEPTED_PIN)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
  await screen.findByRole('list', { name: 'Active tables' })
}

function mainNav() {
  return screen.getByRole('navigation', { name: 'Main' })
}

function navButtons() {
  return within(mainNav()).getAllByRole('button')
}

function heading() {
  return screen.getByRole('heading', { level: 1 })
}

describe('authenticated shell navigation', () => {
  it('shows the main navigation only after login, with Tables as the current screen', async () => {
    const { user } = renderShell()
    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull()

    await logIn(user)
    expect(navButtons().map((button) => button.textContent)).toEqual(['Tables', 'Status'])
    expect(within(mainNav()).getByRole('button', { name: 'Tables' }).getAttribute('aria-current'))
      .toBe('page')
    expect(document.title).toBe('Active tables · POS Mobile')
  })

  it('opens a table from the list, moves focus to its heading and adds it to the navigation', async () => {
    const { user } = renderShell()
    await logIn(user)

    await user.click(screen.getByRole('button', { name: 'Table A2' }))
    expect(heading().textContent).toBe('Table A2')
    expect(document.activeElement).toBe(heading())
    expect(document.title).toBe('Table A2 · POS Mobile')
    expect(screen.getByText('Payment pending')).toBeTruthy()
    expect(
      screen.getByText(
        'Order lines the branch has accepted and bill totals are not shown on this device yet.',
      ),
    ).toBeTruthy()

    expect(navButtons().map((button) => button.textContent)).toEqual([
      'Tables',
      'Table A2',
      'Status',
    ])
    const current = navButtons().filter((button) => button.getAttribute('aria-current') === 'page')
    expect(current.map((button) => button.textContent)).toEqual(['Table A2'])
  })

  it.each(['{Enter}', ' '])('navigates with the keyboard key %j', async (key) => {
    const { user } = renderShell()
    await logIn(user)

    within(mainNav()).getByRole('button', { name: 'Status' }).focus()
    await user.keyboard(key)
    expect(heading().textContent).toBe('Status and outbox')
    expect(document.activeElement).toBe(heading())

    await user.tab()
    expect(document.activeElement).toBe(within(mainNav()).getByRole('button', { name: 'Tables' }))
  })

  it('keeps the current table in the navigation while other screens are shown', async () => {
    const { user } = renderShell()
    await logIn(user)
    await user.click(screen.getByRole('button', { name: 'Table B1' }))

    await user.click(within(mainNav()).getByRole('button', { name: 'Status' }))
    await user.click(within(mainNav()).getByRole('button', { name: 'Table B1' }))
    expect(heading().textContent).toBe('Table B1')
    expect(document.activeElement).toBe(heading())
  })

  it('returns to the previous screen with the browser or Android back action', async () => {
    const { user } = renderShell()
    await logIn(user)
    await user.click(screen.getByRole('button', { name: 'Table A1' }))
    await user.click(within(mainNav()).getByRole('button', { name: 'Status' }))

    act(() => window.history.back())
    await waitFor(() => expect(heading().textContent).toBe('Table A1'))
    expect(document.activeElement).toBe(heading())

    act(() => window.history.back())
    await waitFor(() => expect(heading().textContent).toBe('Active tables'))
  })

  it('does not add a history entry when the current screen is chosen again', async () => {
    const { user } = renderShell()
    await logIn(user)
    const before = window.history.length

    await user.click(within(mainNav()).getByRole('button', { name: 'Tables' }))
    expect(window.history.length).toBe(before)
  })

  it('loads the table list once per login and shares it between screens', async () => {
    const { user, tableLoads } = renderShell()
    await logIn(user)
    await user.click(screen.getByRole('button', { name: 'Table A1' }))
    await user.click(within(mainNav()).getByRole('button', { name: 'Status' }))
    await user.click(within(mainNav()).getByRole('button', { name: 'Tables' }))

    expect(await screen.findByRole('list', { name: 'Active tables' })).toBeTruthy()
    expect(tableLoads()).toBe(1)
  })

  it('says so when the current table is no longer in the latest list', async () => {
    const { user, setListed } = renderShell()
    await logIn(user)
    await user.click(screen.getByRole('button', { name: 'Table A1' }))
    await user.click(within(mainNav()).getByRole('button', { name: 'Tables' }))

    setListed(FIXTURE_ACTIVE_TABLES.filter((table) => table.tableLabel !== 'A1'))
    await user.click(screen.getByRole('button', { name: 'Refresh tables' }))
    await waitFor(() =>
      expect(
        within(screen.getByRole('list', { name: 'Active tables' })).queryByRole('button', {
          name: 'Table A1',
        }),
      ).toBeNull(),
    )

    await user.click(within(mainNav()).getByRole('button', { name: 'Table A1' }))
    expect(
      screen.getByText(
        'Table A1 is not in the last list received from the branch. It may have been closed or moved.',
      ),
    ).toBeTruthy()
  })

  it('shows connection, employee-data sync and each outbox state on the status screen', async () => {
    const { user } = renderShell({ outbox: { draft: 2, pending: 1, submitted: 0, unknown: 3 } })
    await logIn(user)
    await user.click(within(mainNav()).getByRole('button', { name: 'Status' }))

    const outbox = screen.getByRole('region', { name: 'Unsent items on this device' })
    const counts = within(outbox)
      .getAllByRole('term')
      .map((term) => `${term.textContent}=${term.nextElementSibling?.textContent}`)
    expect(counts).toEqual(['Draft=2', 'Pending=1', 'Submitted=0', 'Result unknown=3'])

    const connection = screen.getByRole('region', { name: 'Connection' })
    expect(within(connection).getByText('Branch online')).toBeTruthy()
    expect(within(connection).getByText('Employee data synced')).toBeTruthy()
    const device = screen.getByRole('region', { name: 'This device' })
    expect(within(device).getByText('Fixture handheld 1')).toBeTruthy()
  })

  it('logs out from any screen and starts the next login at the table list', async () => {
    const { user } = renderShell()
    await logIn(user)
    await user.click(screen.getByRole('button', { name: 'Table A1' }))

    await user.click(screen.getByRole('button', { name: 'Log out' }))
    expect(screen.getByRole('heading', { level: 1, name: 'Employee login' })).toBeTruthy()
    expect(screen.queryByRole('navigation', { name: 'Main' })).toBeNull()
    expect(document.title).toBe('POS Mobile')

    await logIn(user)
    expect(heading().textContent).toBe('Active tables')
    expect(navButtons().map((button) => button.textContent)).toEqual(['Tables', 'Status'])
  })

  it('makes each table card one button named after the table', async () => {
    const { user } = renderShell()
    await logIn(user)
    const list = screen.getByRole('list', { name: 'Active tables' })
    const buttons = within(list).getAllByRole('button')
    expect(buttons.map((button) => button.textContent)).toEqual(['Table A1', 'Table A2', 'Table B1'])
    for (const button of buttons) expect(button.getAttribute('type')).toBe('button')
  })
})
