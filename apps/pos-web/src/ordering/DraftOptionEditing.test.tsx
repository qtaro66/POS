import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import type { OrderLine } from '../api/orderingContracts.ts'
import { createOrderingPreview } from './orderingPreview.ts'
import { basketPanelStrings as bs, editorIssueLabels } from './basketPanelStrings.ts'
import * as editorModule from './basketEditor.ts'
type Fixture = ReturnType<typeof createOrderingPreview>
const rice: OrderLine = { sellableItemId: 'DEMO-RICE', quantity: 3, fulfillmentType: 'Takeaway', comment: 'ไม่เผ็ด', options: [{ optionGroupId: 'DEMO-TOPPING', sellableItemId: 'DEMO-EGG', quantity: 1 }] }
async function seed(fixture: Fixture, lines: readonly OrderLine[] = [rice]) {
  return fixture.baskets.save({ ...fixture.scope, sessionMasterDataVersion: fixture.state.menu.masterDataVersion, lines }, null)
}
async function open(user: ReturnType<typeof userEvent.setup>, fixture: Fixture) {
  const view = render(<App client={fixture.client} ordering={{ factory: fixture.factory }} />)
  await user.click(await screen.findByRole('button', { name: /A04/ }))
  await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
  await screen.findAllByRole('article')
  return view
}
const apply = () => within(screen.getByRole('dialog')).getByRole('button', { name: 'ยืนยันแก้ตัวเลือก' })
afterEach(() => vi.restoreAllMocks())

describe('persistent draft option editing', () => {
  it('a late save from a replaced storage adapter cannot close or refocus a newer option editor', async () => {
    const first = createOrderingPreview(), next = createOrderingPreview(), user = userEvent.setup()
    await seed(first); await seed(next)
    let release!: () => void
    const waiting = new Promise<void>(resolve => { release = resolve })
    const create = editorModule.createBasketEditor
    vi.spyOn(editorModule, 'createBasketEditor').mockImplementation((client, session, factory) => {
      const editor = create(client, session, factory)
      return factory !== first.factory ? editor : { ...editor, replaceLine: async (...args) => {
        const saved = await editor.replaceLine(...args); await waiting; return saved
      } }
    })
    const view = await open(user, first)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(apply())
    await screen.findByText('ตะกร้าหรือข้อมูลเมนูเปลี่ยนแล้ว กรุณายกเลิกและเลือกแก้รายการใหม่')
    view.rerender(<App client={first.client} ordering={{ factory: next.factory }} />)
    await waitFor(() => expect((screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }) as HTMLButtonElement).disabled).toBe(false))
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    const dialog = screen.getByRole('dialog')
    await act(async () => { release(); await waiting })
    expect(screen.getByRole('dialog')).toBe(dialog)
    expect(document.activeElement).toBe(dialog)
    expect((apply() as HTMLButtonElement).disabled).toBe(false)
    expect((await next.baskets.load(next.scope))?.revision).toBe(1)
  })

  it('replaces SubItem/Topping in place, preserving quantity, comment, fulfillment and basket identity', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const line: OrderLine = { ...rice, sellableItemId: 'DEMO-STIR', options: [
      { optionGroupId: 'DEMO-SUB', sellableItemId: 'DEMO-CHICKEN', quantity: 1 }, ...rice.options!,
    ] }
    const before = await seed(fixture, [line]); await open(user, fixture)
    const trigger = screen.getByRole('button', { name: 'แก้ตัวเลือก ผัดเปรี้ยวหวาน' })
    await user.click(trigger)
    expect((screen.getByRole('radio', { name: /ไก่ชิ้น/ }) as HTMLInputElement).checked).toBe(true)
    expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('radio', { name: /เต้าหู้/ }))
    const popover = within(screen.getByRole('dialog'))
    expect((popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).value).toBe('1')
    await user.clear(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' })); await user.type(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }), '2')
    await user.click(apply())
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const after = await fixture.baskets.load(fixture.scope)
    expect(after?.basketId).toBe(before.basketId)
    expect(after?.revision).toBe(before.revision + 1)
    expect(after?.lines).toEqual([{ ...line, options: [
      { optionGroupId: 'DEMO-SUB', sellableItemId: 'DEMO-TOFU', quantity: 1 },
      { optionGroupId: 'DEMO-TOPPING', sellableItemId: 'DEMO-EGG', quantity: 2 },
    ] }])
    expect(document.activeElement).toBe(trigger)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })
    expect(JSON.parse(fixture.state.postedBodies[0]).lines).toEqual(after!.lines)
    await user.click(screen.getByRole('tab', { name: 'ส่งแล้ว' }))
    expect(within(screen.getByRole('tabpanel', { name: 'ส่งแล้ว' })).queryByRole('button', { name: /แก้ตัวเลือก/ })).toBeNull()
  })

  it('cancel and Escape never save speculative changes and restore the edit trigger', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const before = await seed(fixture); await open(user, fixture)
    const trigger = screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' })
    await user.click(trigger)
    await user.clear(screen.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' })); await user.type(screen.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }), '2')
    await user.keyboard('{Escape}')
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
    expect(document.activeElement).toBe(trigger)
    expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }) as HTMLButtonElement).disabled).toBe(false)
    await user.click(trigger)
    expect((screen.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).value).toBe('1')
    await user.click(screen.getByRole('button', { name: 'ยกเลิกแก้ตัวเลือก' }))
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
  })

  it('can remove optional toppings and persists the edit across navigation and remount', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    await seed(fixture); const view = await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ล้างตัวเลือกกลุ่มนี้' }))
    await user.click(apply()); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const saved = await fixture.baskets.load(fixture.scope)
    expect(saved?.lines).toEqual([{ ...rice, options: [] }])
    view.unmount(); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    expect((screen.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).value).toBe('0')
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('a quantity edit invalidates the option snapshot instead of overwriting newer line data', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    await seed(fixture); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(screen.getByRole('button', { name: 'เพิ่มจำนวน ข้าวผัดกุ้ง' }))
    await screen.findByText('ตะกร้าหรือข้อมูลเมนูเปลี่ยนแล้ว กรุณายกเลิกและเลือกแก้รายการใหม่')
    expect((apply() as HTMLButtonElement).disabled).toBe(true)
    expect((await fixture.baskets.load(fixture.scope))?.lines).toEqual([{ ...rice, quantity: 4 }])
    await user.click(screen.getByRole('button', { name: 'ยกเลิกแก้ตัวเลือก' }))
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    expect((apply() as HTMLButtonElement).disabled).toBe(false)
  })

  it('removing an earlier row cannot retarget an open option edit to the wrong index', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    await seed(fixture, [{ sellableItemId: 'DEMO-WATER', quantity: 1, fulfillmentType: 'DineIn', comment: null, options: [] }, rice])
    await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(screen.getByRole('button', { name: 'ลบรายการ น้ำเปล่า' }))
    await screen.findByText('ตะกร้าหรือข้อมูลเมนูเปลี่ยนแล้ว กรุณายกเลิกและเลือกแก้รายการใหม่')
    expect((apply() as HTMLButtonElement).disabled).toBe(true)
    expect((await fixture.baskets.load(fixture.scope))?.lines).toEqual([rice])
    await user.click(screen.getByRole('button', { name: 'ยกเลิกแก้ตัวเลือก' }))
    expect(document.activeElement).toBe(screen.getByRole('region', { name: 'ตะกร้าและรายการที่ส่งแล้ว' }))
  })

  it('external-tab compare-and-swap failure preserves the other tab edit and reports a failed save', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const before = await seed(fixture); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    const other = await fixture.baskets.save({ ...fixture.scope, sessionMasterDataVersion: before.sessionMasterDataVersion, lines: [{ ...rice, quantity: 9 }] }, before)
    await user.click(apply())
    await screen.findByText(editorIssueLabels.localWriteFailed)
    expect(await fixture.baskets.load(fixture.scope)).toEqual(other)
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('refreshing the menu while editing invalidates the unsaved selection rather than silently resetting it', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const before = await seed(fixture); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(screen.getByRole('button', { name: bs.refresh }))
    await screen.findByText('ตะกร้าหรือข้อมูลเมนูเปลี่ยนแล้ว กรุณายกเลิกและเลือกแก้รายการใหม่')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
    await user.click(screen.getByRole('button', { name: 'ยกเลิกแก้ตัวเลือก' }))
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    expect((screen.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).value).toBe('1')
  })

  it('can explicitly remove a now-unavailable required topping and select an available replacement', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    fixture.state.menu = { ...fixture.state.menu, items: fixture.state.menu.items.map(item => item.sellableItemId !== 'DEMO-RICE' ? item : {
      ...item, optionGroups: item.optionGroups.map(group => ({ ...group, minSelections: 1, options: [
        { ...group.options[0], availability: { ...group.options[0].availability, soldOut: true } },
        { ...group.options[0], sellableItemId: 'DEMO-CHEESE', displayName: 'ชีส', unitPrice: 20 },
      ] })),
    }) }
    await seed(fixture); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    const popover = within(screen.getByRole('dialog'))
    expect((apply() as HTMLButtonElement).disabled).toBe(true)
    fireEvent.change(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }), { target: { value: '-1' } })
    expect((popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).value).toBe('1')
    await user.clear(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }))
    expect((popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).disabled).toBe(true)
    await user.clear(popover.getByRole('spinbutton', { name: 'จำนวน ชีส' })); await user.type(popover.getByRole('spinbutton', { name: 'จำนวน ชีส' }), '1')
    await user.click(apply()); await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect((await fixture.baskets.load(fixture.scope))?.lines).toEqual([{ ...rice, options: [{ optionGroupId: 'DEMO-TOPPING', sellableItemId: 'DEMO-CHEESE', quantity: 1 }] }])
  })

  it('pending submission freezes option editing and does not create a replacement command', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    fixture.state.postBehavior = 'unavailable'
    await seed(fixture); await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText(bs.pending)
    expect((screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }) as HTMLButtonElement).disabled).toBe(true)
    expect(fixture.state.postedBodies).toHaveLength(1)
  })

  it('leaving an unsaved edit preserves committed options and replacement clients cannot inherit it', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const before = await seed(fixture); const view = await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    await user.click(screen.getByRole('button', { name: 'กลับผังโต๊ะ' }))
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
    await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'แก้ตัวเลือก ข้าวผัดกุ้ง' }))
    const next = createOrderingPreview({ factory: fixture.factory, employeeId: 'PREVIEW-OTHER' })
    view.rerender(<App client={next.client} ordering={{ factory: next.factory }} />)
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(screen.queryByText('กำลังแก้ตัวเลือกรายการเดิม')).toBeNull()
    expect(await next.baskets.load(next.scope)).toBeNull()
  })
})
