import { StrictMode } from 'react'
import { fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from './orderingPreview.ts'
import { basketPanelStrings as bs, editorIssueLabels } from './basketPanelStrings.ts'
import * as editorModule from './basketEditor.ts'

type Fixture = ReturnType<typeof createOrderingPreview>
async function open(user: ReturnType<typeof userEvent.setup>, fixture: Fixture) {
  const view = render(<StrictMode><App client={fixture.client} ordering={{ factory: fixture.factory }} preview /></StrictMode>)
  await user.click(await screen.findByRole('button', { name: /A04/ }))
  await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
  await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })
  await waitFor(() => expect(screen.queryByText(bs.loading)).toBeNull())
  return view
}
async function addWater(user: ReturnType<typeof userEvent.setup>) {
  await user.click(screen.getByRole('button', { name: 'เลือกเมนู น้ำเปล่า' }))
  await user.click(within(screen.getByRole('dialog', { name: 'เลือก Topping' })).getByRole('button', { name: 'ลงตะกร้า' }))
  await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
}
afterEach(() => vi.restoreAllMocks())

describe('PC POS complete ordering workspace', () => {
  it('separates SubItem selection from the Topping popover and persists the original root/option IDs', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture)
    expect((screen.getByRole('button', { name: 'เลือกเมนู ข้าวผัดปู' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(screen.getByRole('button', { name: 'เลือกเมนู ผัดเปรี้ยวหวาน' }))
    expect(screen.queryByRole('dialog')).toBeNull()
    const subitems = within(screen.getByRole('region', { name: 'เลือกรายการย่อย' }))
    expect((subitems.getByRole('button', { name: 'เลือก Topping' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(subitems.getByRole('radio', { name: /ไก่ชิ้น/ }))
    const popover = within(screen.getByRole('dialog', { name: 'เลือก Topping' }))
    expect(popover.queryByRole('radio', { name: /ไก่ชิ้น|เต้าหู้/ })).toBeNull()
    await user.clear(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }))
    await user.type(popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }), '2')
    await user.click(popover.getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const saved = await fixture.baskets.load(fixture.scope)
    expect(saved?.lines).toEqual([{ sellableItemId: 'DEMO-STIR', quantity: 1, fulfillmentType: 'DineIn', comment: null, options: [
      { optionGroupId: 'DEMO-SUB', sellableItemId: 'DEMO-CHICKEN', quantity: 1 },
      { optionGroupId: 'DEMO-TOPPING', sellableItemId: 'DEMO-EGG', quantity: 2 },
    ] }])
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('opens direct Topping selection and blocks fractional or excessive option quantities', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'เลือกเมนู ข้าวผัดกุ้ง' }))
    expect(screen.queryByRole('region', { name: 'เลือกรายการย่อย' })).toBeNull()
    const dialog = screen.getByRole('dialog'), popover = within(dialog)
    const quantity = popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' })
    for (const value of ['3', '1.5', '-1']) {
      fireEvent.change(quantity, { target: { value } })
      expect((popover.getByRole('button', { name: 'ลงตะกร้า' }) as HTMLButtonElement).disabled).toBe(true)
    }
    await user.clear(quantity); await user.type(quantity, '1')
    await user.click(popover.getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect((await fixture.baskets.load(fixture.scope))?.lines[0].options?.[0].quantity).toBe(1)
  })

  it('Escape closes the popover and returns keyboard focus without saving a draft', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture)
    const trigger = screen.getByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })
    await user.click(trigger)
    expect(document.activeElement).toBe(screen.getByRole('dialog'))
    await user.keyboard('{Escape}')
    expect(screen.queryByRole('dialog')).toBeNull()
    expect(document.activeElement).toBe(trigger)
    expect(await fixture.baskets.load(fixture.scope)).toBeNull()
  })

  it('selecting a menu from Sent reveals Basket and its fulfillment choice before a persisted add', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    await open(user, fixture)
    await user.click(screen.getByRole('radio', { name: bs.takeaway }))
    await user.click(screen.getByRole('tab', { name: 'ส่งแล้ว' }))
    await user.click(screen.getByRole('button', { name: 'เลือกเมนู น้ำเปล่า' }))
    expect(screen.getByRole('tab', { name: 'ตะกร้า (0)' }).getAttribute('aria-selected')).toBe('true')
    expect((screen.getByRole('radio', { name: bs.takeaway }) as HTMLInputElement).checked).toBe(true)
    expect(await fixture.baskets.load(fixture.scope)).toBeNull()
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    expect((await fixture.baskets.load(fixture.scope))?.lines[0].fulfillmentType).toBe('Takeaway')
  })

  it('preserves mixed fulfillment and committed edits through tabs, main navigation, back and remount', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    const view = await open(user, fixture)
    await addWater(user)
    await user.click(screen.getByRole('radio', { name: bs.takeaway }))
    await addWater(user)
    expect((await fixture.baskets.load(fixture.scope))?.lines.map(line => line.fulfillmentType)).toEqual(['DineIn', 'Takeaway'])
    await user.click(screen.getByRole('tab', { name: 'ส่งแล้ว' }))
    expect(screen.queryByRole('button', { name: /^ส่งออเดอร์/ })).toBeNull()
    await user.keyboard('{ArrowLeft}')
    expect(screen.getByRole('tab', { name: 'ตะกร้า (2)' }).getAttribute('aria-selected')).toBe('true')
    await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
    await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    await screen.findByRole('tab', { name: 'ตะกร้า (2)' })
    await user.click(screen.getByRole('button', { name: 'กลับผังโต๊ะ' }))
    await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await screen.findByRole('tab', { name: 'ตะกร้า (2)' })
    view.unmount()
    await open(user, fixture)
    await screen.findByRole('tab', { name: 'ตะกร้า (2)' })
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('submits the persisted basket once, retains acceptance, and refreshes authoritative Sent data', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture)
    await addWater(user)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })
    expect(screen.getAllByText(bs.accepted).length).toBeGreaterThanOrEqual(2)
    expect(fixture.state.postedBodies).toHaveLength(1)
    const posted = JSON.parse(fixture.state.postedBodies[0])
    expect(posted.context.tableSessionId).toBe(fixture.scope.tableSessionId)
    expect(posted.lines).toEqual([{ sellableItemId: 'DEMO-WATER', quantity: 1, fulfillmentType: 'DineIn', comment: null, options: [] }])
    await user.click(screen.getByRole('tab', { name: 'ส่งแล้ว' }))
    const sent = within(screen.getByRole('tabpanel', { name: 'ส่งแล้ว' }))
    expect(await sent.findByText('DEMO-BATCH-1')).toBeTruthy()
    expect(sent.getByTestId('net-total').textContent).toBe('20.00 บาท')
    expect(sent.queryByRole('button', { name: /ส่งออเดอร์|เพิ่มจำนวน|ลบรายการ/ })).toBeNull()
    expect((await fixture.baskets.load(fixture.scope))?.lines ?? []).toHaveLength(0)
    expect(fixture.state.postedBodies).toHaveLength(1)
  })

  it('lost acknowledgement remains pending until explicit GET recovery, including after remount', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    fixture.state.postBehavior = 'lostAfterAccept'
    const view = await open(user, fixture)
    await addWater(user)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText(bs.pending)
    const pending = await fixture.baskets.load(fixture.scope)
    expect(screen.queryByRole('button', { name: /^ส่งออเดอร์/ })).toBeNull()
    expect(pending?.pendingRequestId).toBeTruthy()
    expect(screen.queryByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })).toBeNull()
    view.unmount()
    await open(user, fixture)
    expect(fixture.state.postedBodies).toHaveLength(1)
    await user.click(await screen.findByRole('button', { name: bs.check }))
    await screen.findByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })
    expect(fixture.state.postedBodies).toHaveLength(1)
    expect(screen.getAllByText(new RegExp(pending!.pendingRequestId!)).length).toBeGreaterThan(0)
  })

  it('retries only on explicit request using the original body and request ID', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    fixture.state.postBehavior = 'unavailable'
    await open(user, fixture); await addWater(user)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText(bs.pending)
    expect((screen.getByRole('button', { name: 'เลือกเมนู น้ำเปล่า' }) as HTMLButtonElement).disabled).toBe(true)
    fixture.state.postBehavior = 'accept'
    await user.click(screen.getByRole('button', { name: bs.retry }))
    await screen.findByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })
    expect(fixture.state.postedBodies).toHaveLength(2)
    expect(fixture.state.postedBodies[1]).toBe(fixture.state.postedBodies[0])
  })

  it('rejection keeps the draft without claiming acceptance or refreshing it into Sent', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    fixture.state.postBehavior = 'reject'
    await open(user, fixture); await addWater(user)
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText(bs.rejected)
    expect(screen.queryByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })).toBeNull()
    expect((await fixture.baskets.load(fixture.scope))?.lines).toHaveLength(1)
    await user.click(screen.getByRole('tab', { name: 'ส่งแล้ว' }))
    expect(screen.queryByText('DEMO-BATCH-1')).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
  })

  it('blocks additions and submission after a frozen catalog mismatch without repricing the stored draft', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture); await addWater(user)
    const saved = await fixture.baskets.load(fixture.scope)
    fixture.state.menu = { ...fixture.state.menu, masterDataVersion: 'DEMO-NEW' }
    await user.click(screen.getByRole('button', { name: bs.refresh }))
    await screen.findByText(editorIssueLabels.catalogChanged)
    expect(screen.queryByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })).toBeNull()
    expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }) as HTMLButtonElement).disabled).toBe(true)
    expect(await fixture.baskets.load(fixture.scope)).toEqual(saved)
  })

  it('an unavailable required Topping cannot be bypassed in the popover', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    fixture.state.menu = { ...fixture.state.menu, items: fixture.state.menu.items.map(item => item.sellableItemId !== 'DEMO-RICE' ? item : {
      ...item, optionGroups: item.optionGroups.map(group => ({ ...group, minSelections: 1, options: group.options.map(option => ({ ...option, availability: { ...option.availability, soldOut: true } })) })),
    }) }
    await open(user, fixture)
    await user.click(screen.getByRole('button', { name: 'เลือกเมนู ข้าวผัดกุ้ง' }))
    const popover = within(screen.getByRole('dialog'))
    expect((popover.getByRole('spinbutton', { name: 'จำนวน ไข่ดาว' }) as HTMLInputElement).disabled).toBe(true)
    expect((popover.getByRole('button', { name: 'ลงตะกร้า' }) as HTMLButtonElement).disabled).toBe(true)
    expect(await fixture.baskets.load(fixture.scope)).toBeNull()
  })

  it('availability changing after a persisted add blocks the entire submission and preserves the draft', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    await open(user, fixture); await addWater(user)
    const saved = await fixture.baskets.load(fixture.scope)
    fixture.state.menu = { ...fixture.state.menu, items: fixture.state.menu.items.map(item => item.sellableItemId !== 'DEMO-WATER' ? item : { ...item, availability: { ...item.availability, soldOut: true } }) }
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText('สินค้าหรือตัวเลือกบางรายการยังไม่พร้อมขาย โปรดตรวจสอบตะกร้า')
    expect(fixture.state.postedBodies).toHaveLength(0)
    expect(await fixture.baskets.load(fixture.scope)).toEqual(saved)
  })

  it('storage failure is not an editable empty basket and does not expose submit or menu actions', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    vi.spyOn(fixture.factory, 'open').mockImplementation(() => { throw new Error('Synthetic unavailable storage') })
    render(<App client={fixture.client} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
    await screen.findByText(editorIssueLabels.storageUnavailable)
    expect(screen.queryByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })).toBeNull()
    expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 0 รายการ' }) as HTMLButtonElement).disabled).toBe(true)
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('a lock discovered at submit preflight prevents sending and retains the draft', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    await open(user, fixture); await addWater(user)
    fixture.state.locked = true
    await user.click(screen.getByRole('button', { name: 'ส่งออเดอร์ 1 รายการ' }))
    await screen.findByText(editorIssueLabels.sessionLocked)
    expect(fixture.state.postedBodies).toHaveLength(0)
    expect((await fixture.baskets.load(fixture.scope))?.lines).toHaveLength(1)
  })

  it('a new employee client on the same device cannot inherit draft or selection UI', async () => {
    const user = userEvent.setup(), first = createOrderingPreview()
    const view = await open(user, first); await addWater(user)
    await user.click(screen.getByRole('button', { name: 'เลือกเมนู ผัดเปรี้ยวหวาน' }))
    const next = createOrderingPreview({ factory: first.factory, employeeId: 'PREVIEW-OTHER' })
    view.rerender(<App client={next.client} ordering={{ factory: next.factory }} />)
    expect(screen.queryByRole('tab')).toBeNull()
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
    await screen.findByText(bs.empty)
    expect(screen.queryByRole('region', { name: 'เลือกรายการย่อย' })).toBeNull()
    expect((await first.baskets.load(first.scope))?.lines).toHaveLength(1)
    expect(await next.baskets.load(next.scope)).toBeNull()
  })

  it('keeps durable acceptance and local-write warnings while refreshing the independent read view', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    const original = editorModule.createBasketEditor(fixture.client, fixture.scope.tableSessionId, fixture.factory)
    const state: editorModule.BasketEditorState = { ...original.getSnapshot(), phase: 'ready', issue: 'storageUnavailable', outcome: {
      kind: 'durable', requestId: 'DEMO-LOCAL-WARNING', localApplied: false,
      response: { result: { schemaVersion: 1, requestId: 'DEMO-LOCAL-WARNING', status: 'BranchAccepted', tableSessionId: fixture.scope.tableSessionId, tableSessionStatus: 'Ordering', tableVersion: 2, errorCode: null, serverReceivedAt: '2026-11-01T09:30:00Z', branchSequence: 2 }, validationDetails: null },
    } }
    vi.spyOn(editorModule, 'createBasketEditor').mockReturnValue({ ...original, refresh: async () => {}, cancel: () => {}, getSnapshot: () => state, subscribe: () => () => {} })
    render(<App client={fixture.client} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
    await screen.findByRole('button', { name: 'ปิดข้อความรับออร์เดอร์' })
    expect(screen.getByText(bs.localWarning)).toBeTruthy()
    expect(screen.getByText(/ข้อมูลในเครื่องยังปรับปรุงไม่ครบ/)).toBeTruthy()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })
})
