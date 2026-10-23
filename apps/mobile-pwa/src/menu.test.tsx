import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it } from 'vitest'
import App from './App.tsx'
import { FIXTURE_MENU } from './adapters/fixtureMenu.ts'
import {
  createFixtureServices,
  FIXTURE_ACCEPTED_PIN,
  FIXTURE_DEVICE,
  type FixtureOptions,
} from './adapters/fixtures.ts'
import type { MenuGateway } from './adapters/ports.ts'
import { Basket } from './components/Basket.tsx'
import type { Menu } from './domain/menu.ts'

type User = ReturnType<typeof userEvent.setup>

function renderApp(options: FixtureOptions = {}, menu?: MenuGateway) {
  const fixtures = createFixtureServices({ pairedDevice: FIXTURE_DEVICE, ...options })
  const services = menu ? { ...fixtures, menu } : fixtures
  render(<App services={services} initialDevice={FIXTURE_DEVICE} />)
  return { services, user: userEvent.setup() }
}

async function openTable(user: User, label = 'A1') {
  await user.type(screen.getByLabelText('PIN'), FIXTURE_ACCEPTED_PIN)
  await user.click(screen.getByRole('button', { name: 'Log in' }))
  await user.click(await screen.findByRole('button', { name: `Table ${label}` }))
}

async function openMenu(user: User) {
  await openTable(user)
  await user.click(screen.getByRole('button', { name: 'Add items' }))
  return screen.findByRole('list', { name: 'Menu · Table A1' })
}

function heading() {
  return screen.getByRole('heading', { level: 1 })
}

function menuItem(name: RegExp) {
  return within(screen.getByRole('list', { name: 'Menu · Table A1' })).getByRole('button', { name })
}

function basket() {
  return screen.getByRole('region', { name: 'Basket' })
}

async function chooseBasilWithToppings(user: User) {
  await user.click(menuItem(/Basil stir-fry/))
  await user.click(screen.getByRole('button', { name: 'Add one Fried egg' }))
  await user.click(screen.getByRole('button', { name: 'Add one Fried egg' }))
  await user.click(screen.getByRole('button', { name: 'Add one Cheese' }))
  await user.click(screen.getByRole('button', { name: 'Add to basket' }))
  await screen.findByRole('list', { name: 'Menu · Table A1' })
}

describe('menu', () => {
  it('opens from an Ordering table and moves focus to its heading', async () => {
    const { user } = renderApp()
    await openMenu(user)
    expect(heading().textContent).toBe('Menu · Table A1')
    expect(document.activeElement).toBe(heading())
    expect(
      within(screen.getByRole('navigation', { name: 'Main' }))
        .getByRole('button', { name: 'Table A1' })
        .getAttribute('aria-current'),
    ).toBe('page')
  })

  it('does not offer adding items when the table is not Ordering (D03, D20.2)', async () => {
    const { user } = renderApp()
    await openTable(user, 'A2')
    expect(screen.queryByRole('button', { name: 'Add items' })).toBeNull()
    expect(screen.getByText(/Items can be added only while the table is Ordering/)).toBeTruthy()
  })

  it('shows code, price and Sold Out, and filters by category (D18.6)', async () => {
    const { user } = renderApp()
    await openMenu(user)

    const garlic = menuItem(/Garlic stir-fry/)
    expect(garlic.textContent).toContain('F02')
    expect(garlic.textContent).toContain('Sold out')
    expect(garlic.getAttribute('aria-disabled')).toBe('true')

    await user.click(screen.getByRole('button', { name: 'Drinks' }))
    expect(screen.getByRole('button', { name: 'Drinks' }).getAttribute('aria-pressed')).toBe('true')
    const names = within(screen.getByRole('list', { name: 'Menu · Table A1' }))
      .getAllByRole('button')
      .map((button) => button.querySelector('.menu-item__name')?.textContent)
    expect(names).toEqual(['Water', 'Iced tea'])
  })

  it('does not add a Sold Out item', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Garlic stir-fry/))
    expect(heading().textContent).toBe('Menu · Table A1')
    expect(screen.getByRole('button', { name: 'View basket (0)' })).toBeTruthy()
  })

  it('adds an item without options with one tap and confirms it politely', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Water/))
    await user.click(menuItem(/Water/))

    expect(screen.getByRole('button', { name: 'View basket (2)' })).toBeTruthy()
    await waitFor(() =>
      expect(document.querySelector('.live-message')?.textContent).toBe(
        'Water added. 2 items in the basket.',
      ),
    )
    expect(document.querySelector('.live-message')?.getAttribute('aria-live')).toBe('polite')
  })

  it('reports an unreachable menu and loads it on retry', async () => {
    let calls = 0
    const { user } = renderApp(
      {},
      {
        loadMenu: () => {
          calls += 1
          return Promise.resolve(
            calls === 1 ? { kind: 'unreachable' as const } : { kind: 'ok' as const, menu: FIXTURE_MENU },
          )
        },
      },
    )
    await openTable(user)
    await user.click(screen.getByRole('button', { name: 'Add items' }))

    expect((await screen.findByRole('alert')).textContent).toContain('could not be loaded')
    await user.click(screen.getByRole('button', { name: 'Try again' }))
    expect(await screen.findByRole('list', { name: 'Menu · Table A1' })).toBeTruthy()
  })
})

describe('item options (D08.4)', () => {
  it('shows SubItem groups before Toppings with their rules and preselects the default', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Basil stir-fry/))

    expect(heading().textContent).toBe('Basil stir-fry')
    const groups = screen.getAllByRole('group').filter((group) => group.tagName === 'FIELDSET')
    expect(groups.map((group) => group.querySelector('legend')?.textContent)).toEqual([
      'Protein',
      'Toppings',
    ])
    expect(within(groups[0]).getByText('Required · choose 1')).toBeTruthy()
    expect(within(groups[1]).getByText(/Optional · choose up to 3 · 0 of 3 chosen/)).toBeTruthy()
    expect((screen.getByRole('radio', { name: /Chicken/ }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('radio', { name: /Squid/ }) as HTMLInputElement).disabled).toBe(true)
  })

  it('limits repeats per option and the group total, counted by quantity', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Basil stir-fry/))

    const addEgg = screen.getByRole('button', { name: 'Add one Fried egg' }) as HTMLButtonElement
    await user.click(addEgg)
    await user.click(addEgg)
    expect(addEgg.disabled).toBe(true)

    await user.click(screen.getByRole('button', { name: 'Add one Cheese' }))
    expect(screen.getByText(/3 of 3 chosen/)).toBeTruthy()
    expect((screen.getByRole('button', { name: 'Add one Extra chili' }) as HTMLButtonElement).disabled)
      .toBe(true)
  })

  it('refuses a required group left empty and moves focus to it', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Spring rolls/))
    await user.click(screen.getByRole('button', { name: 'Add to basket' }))

    expect(screen.getByRole('alert').textContent).toBe('Check the highlighted choices.')
    const sauce = screen.getByRole('group', { name: 'Dipping sauce' })
    expect(document.activeElement).toBe(sauce)
    expect(sauce.getAttribute('aria-invalid')).toBe('true')
    expect(within(sauce).getByText('Choose at least 1.')).toBeTruthy()
    expect(heading().textContent).toBe('Spring rolls')
  })

  it('disables further checkboxes once a group without repeats is full', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Spring rolls/))
    await user.click(screen.getByRole('checkbox', { name: /Sweet chili/ }))
    await user.click(screen.getByRole('checkbox', { name: /Seafood sauce/ }))

    expect((screen.getByRole('checkbox', { name: /Plum sauce/ }) as HTMLInputElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'Add to basket' }))
    expect(await screen.findByRole('button', { name: 'View basket (1)' })).toBeTruthy()
  })

  it('adds the line with quantity and note, then returns to the menu', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Basil stir-fry/))
    await user.click(screen.getByRole('radio', { name: /Pork/ }))
    await user.click(screen.getByRole('button', { name: 'Increase quantity' }))
    await user.type(screen.getByLabelText('Note for the kitchen'), '  not spicy  ')
    await user.click(screen.getByRole('button', { name: 'Add to basket' }))

    expect(await screen.findByRole('button', { name: 'View basket (2)' })).toBeTruthy()
    expect(document.activeElement).toBe(heading())
    await user.click(screen.getByRole('button', { name: 'View basket (2)' }))
    const line = within(basket()).getByRole('listitem')
    expect(line.textContent).toContain('Basil stir-fry')
    expect(line.textContent).toContain('Pork')
    expect(line.textContent).toContain('Note: not spicy')
    expect(within(line).getByRole('group', { name: 'Quantity of Basil stir-fry' }).textContent).toContain('2')
  })

  it('leaves the basket unchanged when the options screen is left with Back', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Basil stir-fry/))
    act(() => window.history.back())
    await waitFor(() => expect(heading().textContent).toBe('Menu · Table A1'))
    expect(screen.getByRole('button', { name: 'View basket (0)' })).toBeTruthy()
  })
})

describe('basket', () => {
  it('summarizes options, changes quantity and never goes below 1', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await chooseBasilWithToppings(user)
    await user.click(screen.getByRole('button', { name: 'View basket (1)' }))

    const line = within(basket()).getByRole('listitem')
    expect(line.textContent).toMatch(/Chicken, Fried egg ×2 \+.*10\.00, Cheese \+.*15\.00/)
    const decrease = within(line).getByRole('button', { name: 'Decrease Basil stir-fry' }) as HTMLButtonElement
    expect(decrease.disabled).toBe(true)
    await user.click(within(line).getByRole('button', { name: 'Increase Basil stir-fry' }))
    expect(within(basket()).getByText('2 items in the basket')).toBeTruthy()
    await user.click(decrease)
    expect(within(basket()).getByText('1 item in the basket')).toBeTruthy()
  })

  it('edits a line in place instead of adding a new one', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await chooseBasilWithToppings(user)
    await user.click(screen.getByRole('button', { name: 'View basket (1)' }))

    await user.click(within(basket()).getByRole('button', { name: 'Edit Basil stir-fry' }))
    expect(screen.getByText(/3 of 3 chosen/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Remove one Cheese' }))
    await user.click(screen.getByRole('button', { name: 'Update basket' }))

    await waitFor(() => expect(heading().textContent).toBe('Table A1'))
    const lines = within(basket()).getAllByRole('listitem')
    expect(lines).toHaveLength(1)
    expect(lines[0].textContent).not.toContain('Cheese')
  })

  it('removes a line, keeps focus in the basket and confirms the removal', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Water/))
    await user.click(screen.getByRole('button', { name: 'View basket (1)' }))

    await user.click(within(basket()).getByRole('button', { name: 'Remove Water' }))
    expect(within(basket()).getByText('The basket is empty.')).toBeTruthy()
    expect(document.activeElement).toBe(within(basket()).getByRole('heading', { name: 'Basket' }))
    await waitFor(() =>
      expect(document.querySelector('.live-message')?.textContent).toBe('Water removed from the basket.'),
    )
  })

  it('says the basket is kept only while the app is open and shows no total (D10.1)', async () => {
    const { user } = renderApp()
    await openMenu(user)
    await user.click(menuItem(/Iced tea/))
    await user.click(screen.getByRole('button', { name: 'View basket (1)' }))

    expect(within(basket()).getByText(/kept only while this app stays open/)).toBeTruthy()
    expect(within(basket()).queryByText(/total/i)).toBeNull()
  })

  it('keeps each table session’s basket separate', async () => {
    const { user } = renderApp({
      tables: [
        {
          tableId: 't1',
          tableLabel: 'A1',
          tableSessionId: 's1',
          status: 'Ordering',
          tableVersion: 1,
          openedAt: '2026-01-15T02:40:00.000Z',
        },
        {
          tableId: 't2',
          tableLabel: 'B1',
          tableSessionId: 's2',
          status: 'Ordering',
          tableVersion: 1,
          openedAt: '2026-01-15T02:40:00.000Z',
        },
      ],
    })
    await openMenu(user)
    await user.click(menuItem(/Water/))

    await user.click(within(screen.getByRole('navigation', { name: 'Main' })).getByRole('button', { name: 'Tables' }))
    await user.click(await screen.findByRole('button', { name: 'Table B1' }))
    expect(await within(basket()).findByText('The basket is empty.')).toBeTruthy()
  })

  it('flags a line that is no longer available in the current menu', () => {
    const soldOutWater: Menu = {
      ...FIXTURE_MENU,
      items: FIXTURE_MENU.items.map((item) =>
        item.sellableItemId === 'fixture-item-water' ? { ...item, availability: 'soldOut' } : item,
      ),
    }
    render(
      <Basket
        lines={[{ lineId: 'l1', sellableItemId: 'fixture-item-water', quantity: 1, selections: [], comment: '' }]}
        menu={soldOutWater}
        persistence="memory"
        saving={false}
        saveFailed={false}
        needsReview={false}
        onQuantity={() => undefined}
        onEdit={() => undefined}
        onRemove={() => undefined}
      />,
    )
    expect(screen.getByText('No longer available. Remove it or choose another item.')).toBeTruthy()
  })
})
