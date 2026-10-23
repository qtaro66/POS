import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { IDBFactory } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import {
  createFixtureServices,
  FIXTURE_ACCEPTED_PIN,
  FIXTURE_DEVICE,
  FIXTURE_EMPLOYEE,
} from './adapters/fixtures.ts'
import { IndexedDbOrderStore } from './adapters/indexedDbOrderStore.ts'
import { withPersistentOrders } from './adapters/persistentOrders.ts'
import { addLine, emptyDraft, type DraftKey } from './domain/draft.ts'
import { newSubmitOrderRequest } from './domain/mobileRequest.ts'

type User = ReturnType<typeof userEvent.setup>

const NOW = new Date('2026-01-15T03:00:00.000Z')
const now = () => NOW
const DAY_MS = 24 * 60 * 60 * 1000
const tableA1: DraftKey = {
  deviceId: FIXTURE_DEVICE.deviceId,
  employeeId: FIXTURE_EMPLOYEE.employeeId,
  tableSessionId: 'fixture-session-0001',
}
const waterLine = {
  lineId: 'line-1',
  sellableItemId: 'fixture-item-water',
  quantity: 1,
  selections: [],
  comment: '',
}

/** Starts the app as `main.tsx` does, on the given IndexedDB, and returns a user. */
async function startApp(factory: IDBFactory) {
  const services = await withPersistentOrders(
    createFixtureServices({ pairedDevice: FIXTURE_DEVICE }),
    now,
    factory,
  )
  const view = render(<App services={services} initialDevice={FIXTURE_DEVICE} now={now} />)
  return { user: userEvent.setup(), unmount: view.unmount }
}

async function logIn(user: User) {
  await user.type(screen.getByLabelText('PIN'), FIXTURE_ACCEPTED_PIN)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
  await screen.findByRole('list', { name: 'Active tables' })
}

async function openTableA1(user: User) {
  await user.click(screen.getByRole('button', { name: 'Table A1' }))
  return screen.findByRole('region', { name: 'Basket' })
}

describe('persistent basket (D10.5)', () => {
  it('keeps the basket on the device across an app restart', async () => {
    const factory = new IDBFactory()
    const first = await startApp(factory)
    await logIn(first.user)
    await openTableA1(first.user)
    await first.user.click(screen.getByRole('button', { name: 'Add items' }))
    await first.user.click(await screen.findByRole('button', { name: /Water/ }))
    await first.user.click(screen.getByRole('button', { name: 'View basket (1)' }))
    expect(
      await within(screen.getByRole('region', { name: 'Basket' })).findByText(
        'The basket is saved on this device.',
      ),
    ).toBeTruthy()
    first.unmount()

    const second = await startApp(factory)
    await logIn(second.user)
    const basket = await openTableA1(second.user)
    expect(await within(basket).findByText('1 item in the basket')).toBeTruthy()
    expect(within(basket).getByRole('listitem').textContent).toContain('Water')
  })

  it('keeps the basket on logout and counts it as unsent (D10.5)', async () => {
    const factory = new IDBFactory()
    const { user } = await startApp(factory)
    await logIn(user)
    await openTableA1(user)
    await user.click(screen.getByRole('button', { name: 'Add items' }))
    await user.click(await screen.findByRole('button', { name: /Water/ }))

    await waitFor(() => expect(screen.getByRole('status').textContent).toContain('1 unsent item'))
    await user.click(screen.getByRole('button', { name: 'Log out' }))
    expect(screen.getByText('1 unsent item is saved on this device.')).toBeTruthy()
    expect(await new IndexedDbOrderStore(factory).load(tableA1)).not.toBeNull()
  })

  it('marks a basket started more than 30 days ago for review without deleting it', async () => {
    const factory = new IDBFactory()
    const old = new Date(NOW.getTime() - 31 * DAY_MS)
    await new IndexedDbOrderStore(factory).save(addLine(emptyDraft(tableA1, 'fixture-md-0001', old), waterLine))

    const { user } = await startApp(factory)
    await logIn(user)
    const basket = await openTableA1(user)
    expect(
      await within(basket).findByText(
        'This basket was started more than 30 days ago and needs review before it can be sent.',
      ),
    ).toBeTruthy()
    expect(within(basket).getByRole('listitem').textContent).toContain('Water')
    expect((await new IndexedDbOrderStore(factory).load(tableA1))?.reviewState).toBe(
      'ExpiredNeedsReview',
    )
  })

  it('shows stored outbox requests by state on the status screen', async () => {
    const factory = new IDBFactory()
    const store = new IndexedDbOrderStore(factory)
    const draft = addLine(emptyDraft(tableA1, 'fixture-md-0001', NOW), waterLine)
    await store.save(draft)
    await store.submitDraft(tableA1, newSubmitOrderRequest('request-1', draft, NOW))

    const { user } = await startApp(factory)
    expect(screen.getByText('1 unsent item is saved on this device.')).toBeTruthy()
    await logIn(user)
    await user.click(
      within(screen.getByRole('navigation', { name: 'Main' })).getByRole('button', { name: 'Status' }),
    )
    const outbox = screen.getByRole('region', { name: 'Unsent items on this device' })
    const counts = within(outbox)
      .getAllByRole('term')
      .map((term) => `${term.textContent}=${term.nextElementSibling?.textContent}`)
    expect(counts).toEqual(['Draft=0', 'Pending=1', 'Submitted=0', 'Result unknown=0'])
  })
})
