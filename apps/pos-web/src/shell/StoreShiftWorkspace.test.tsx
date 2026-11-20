import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { afterEach, beforeAll, expect, it, vi } from 'vitest'
import { StoreShiftWorkspace } from './StoreShiftWorkspace.tsx'
import type { StoreLifecycleCommand, StoreLifecycleState } from '../api/storeLifecycleContracts.ts'

const store = '00000000-0000-4000-8000-000000000001'
const shift = '00000000-0000-4000-8000-000000000002'
const state: StoreLifecycleState = { storeSessionId: store, activeShiftId: shift, isClosed: false, businessDate: '2026-11-19', openingCashAmount: 500, closing: null }
beforeAll(() => {
  HTMLDialogElement.prototype.showModal = function () { this.open = true }
})
afterEach(() => localStorage.clear())
const key = 'store-test'
const current = async () => ({ kind: 'ok' as const, value: state })

it('opens confirmation only from allowed actions and keeps a nullable shift count and PIN out of storage', async () => {
  const command = vi.fn(async (body: StoreLifecycleCommand) => ({
    status: 200, body: { ...state, activeShiftId: null, closing: {
      storeSessionId: store, shiftId: shift, closesStore: false,
      expectedCashAmount: 500, actualCashAmount: null, cashDifference: null,
      reconciliationStatus: 'NotReconciled', context: { employeeId: 'E2', deviceId: 'D1', occurredAt: '2026-11-19T01:00:00Z', branchLocalDate: '2026-11-19' },
    } },
    input: body,
  }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  expect(screen.queryByLabelText('ยืนยัน PIN')).toBeNull()
  expect(screen.queryByRole('button', { name: 'เปิดร้าน' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'ปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  expect(dialog.getByText(/เมื่อยืนยันแล้วจะแก้ไขไม่ได้/)).toBeTruthy()
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดกะ' }).closest('form')!)
  await screen.findByText('ร้านเปิด · ระหว่างกะ')
  expect(command).toHaveBeenCalledWith(expect.objectContaining({ operation: 'CloseShift', cashAmount: null, storeSessionId: store, shiftId: shift }), '123456')
  expect(localStorage.getItem(key)).toBeNull()
  expect(screen.getByRole('button', { name: 'เปิดกะ' })).toBeTruthy()
})

it('preserves the same uncertain command across reload and requires a fresh PIN before retrying', async () => {
  const calls: StoreLifecycleCommand[] = []
  const command = vi.fn(async (body: StoreLifecycleCommand, pin: string) => {
    calls.push(body)
    expect(pin).toBe('123456')
    const stored = localStorage.getItem(key)!
    expect(stored).not.toContain('123456')
    expect(stored).not.toContain('pin')
    return { reason: 'network' as const }
  })
  const port = { storeLifecycleCurrent: current, storeLifecycleCommand: command }
  const view = render(<StoreShiftWorkspace storageKey={key} port={port} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  fireEvent.click(screen.getByRole('button', { name: 'ปิดร้าน' }))
  let dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยอดเงินสดนับจริง'), { target: { value: '500' } })
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดร้าน' }).closest('form')!)
  await waitFor(() => expect(command).toHaveBeenCalledTimes(1))
  await waitFor(() => expect((dialog.getByLabelText('ยืนยัน PIN') as HTMLInputElement).value).toBe(''))
  view.unmount()
  render(<StoreShiftWorkspace storageKey={key} port={port} />)
  await screen.findByRole('button', { name: 'ยืนยันคำขอเดิม' })
  expect(screen.queryByRole('button', { name: 'ปิดร้าน' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'ยืนยันคำขอเดิม' }))
  dialog = within(screen.getByRole('dialog'))
  expect((dialog.getByLabelText('ยอดเงินสดนับจริง') as HTMLInputElement).readOnly).toBe(true)
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดร้าน' }).closest('form')!)
  await waitFor(() => expect(command).toHaveBeenCalledTimes(2))
  expect(calls[1]).toEqual(calls[0])
})

it('blocks parallel confirmations until the original request finishes', async () => {
  let finish!: (result: { reason: 'network' }) => void
  const command = vi.fn(() => new Promise<{ reason: 'network' }>(resolve => { finish = resolve }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  fireEvent.click(screen.getByRole('button', { name: 'ปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  const form = dialog.getByRole('button', { name: 'ยืนยันปิดกะ' }).closest('form')!
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(form)
  fireEvent.submit(form)
  expect(command).toHaveBeenCalledTimes(1)
  await act(async () => finish({ reason: 'network' }))
})

it('blocks commands for corrupt local pending data', async () => {
  localStorage.setItem(key, '{')
  const command = vi.fn()
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  expect(screen.getByRole('alert').textContent).toContain('ข้อมูลคำขอในเครื่องยังไม่พร้อม')
  expect(screen.queryByRole('button', { name: 'ปิดร้าน' })).toBeNull()
})

it('allows correcting a PIN after a definite first-attempt denial', async () => {
  const command = vi.fn()
    .mockResolvedValueOnce({ status: 401, body: null })
    .mockResolvedValueOnce({ status: 200, body: { ...state, activeShiftId: null } })
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  fireEvent.click(screen.getByRole('button', { name: 'ปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  const pin = dialog.getByLabelText('ยืนยัน PIN')
  const form = dialog.getByRole('button', { name: 'ยืนยันปิดกะ' }).closest('form')!
  fireEvent.change(pin, { target: { value: 'wrong' } })
  fireEvent.submit(form)
  await waitFor(() => expect(command).toHaveBeenCalledTimes(1))
  await waitFor(() => expect(localStorage.getItem(key)).toBeNull())
  fireEvent.change(dialog.getByLabelText('ยอดเงินสดนับจริง'), { target: { value: '500' } })
  fireEvent.change(pin, { target: { value: '123456' } })
  fireEvent.submit(form)
  await waitFor(() => expect(command).toHaveBeenCalledTimes(2))
  expect(command.mock.calls[1][0].cashAmount).toBe(500)
  expect(command.mock.calls[1][0].requestId).not.toBe(command.mock.calls[0][0].requestId)
})

it('keeps a recovered uncertain command when the original actor PIN conflicts', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const command = vi.fn(async () => ({ status: 409, body: null }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByRole('button', { name: 'ยืนยันคำขอเดิม' })
  fireEvent.click(screen.getByRole('button', { name: 'ยืนยันคำขอเดิม' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดร้าน' }).closest('form')!)
  await waitFor(() => expect(command).toHaveBeenCalledTimes(1))
  expect(JSON.parse(localStorage.getItem(key)!)).toEqual(saved)
  expect(screen.queryByRole('button', { name: 'เปิดร้าน' })).toBeNull()
})

it('is reachable through the normal store-and-shift navigation', async () => {
  const fixture = createOrderingPreview()
  const client = { ...fixture.client, storeLifecycleCurrent: current, storeLifecycleCommand: vi.fn() }
  render(<App client={client} />)
  fireEvent.click(screen.getByRole('button', { name: 'ร้านและกะ' }))
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  expect(screen.getByRole('button', { name: 'ปิดกะ' })).toBeTruthy()
  expect(screen.queryByLabelText('ยืนยัน PIN')).toBeNull()
})
it('refreshes authoritative state after a replay returns an older saved result', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'OpenStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const latest = { ...state, isClosed: true, activeShiftId: null }
  const read = vi.fn(async () => ({ kind: 'ok' as const, value: latest }))
  const command = vi.fn(async () => ({ status: 200, body: state }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: read, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านปิด')
  fireEvent.click(screen.getByRole('button', { name: 'ยืนยันคำขอเดิม' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันเปิดร้าน' }).closest('form')!)
  await waitFor(() => expect(read).toHaveBeenCalledTimes(2))
  await screen.findByRole('button', { name: 'เปิดร้าน' })
  expect(screen.getByText('ร้านปิด')).toBeTruthy()
  expect(localStorage.getItem(key)).toBeNull()
})

it('recovers a saved command after reload by checking its durable result and reading the latest store', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'OpenStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const closed = { ...state, isClosed: true, activeShiftId: null }
  const read = vi.fn(async () => ({ kind: 'ok' as const, value: closed }))
  const status = vi.fn(async () => ({ status: 200, body: { status: 'Completed' as const, result: state } }))
  const command = vi.fn()
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: read, storeLifecycleCommand: command, storeLifecycleRequestStatus: status }} />)
  await screen.findByRole('button', { name: 'ตรวจสอบผลคำขอ' })
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบผลคำขอ' }))
  await screen.findByRole('button', { name: 'เปิดร้าน' })
  expect(status).toHaveBeenCalledWith(saved)
  expect(command).not.toHaveBeenCalled()
  expect(read).toHaveBeenCalledTimes(2)
  expect(screen.getByText('ร้านปิด')).toBeTruthy()
  expect(localStorage.getItem(key)).toBeNull()
})

it.each([
  { status: 200, body: { status: 'NotFound', result: null } },
  { status: 404, body: null },
  { reason: 'protocol' as const },
])('retains a pending command when status cannot prove completion or retirement', async result => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const status = vi.fn(async () => result)
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: vi.fn(), storeLifecycleRequestStatus: status }} />)
  await screen.findByRole('button', { name: 'ตรวจสอบผลคำขอ' })
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบผลคำขอ' }))
  await waitFor(() => expect(status).toHaveBeenCalledTimes(1))
  expect(JSON.parse(localStorage.getItem(key)!)).toEqual(saved)
  expect(screen.queryByRole('button', { name: 'ปิดร้าน' })).toBeNull()
})

it('retires an unresolved command through the Branch and uses a fresh transient PIN', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const retire = vi.fn(async () => ({ status: 200, body: { status: 'Retired' as const, result: null } }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: vi.fn(), retireStoreLifecycleRequest: retire }} />)
  await screen.findByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' })
  fireEvent.click(screen.getByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' }))
  const dialog = within(screen.getByRole('dialog'))
  expect(dialog.getByText(/ป้องกันการส่งคำขอเดิมซ้ำ/)).toBeTruthy()
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันยกเลิกคำขอ' }).closest('form')!)
  await screen.findByRole('button', { name: 'ปิดร้าน' })
  expect(retire).toHaveBeenCalledWith(saved, '123456')
  expect(localStorage.getItem(key)).toBeNull()
})

it('preserves unresolved command and clears typed PIN when retirement fails', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const retire = vi.fn(async () => ({ reason: 'network' as const }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: vi.fn(), retireStoreLifecycleRequest: retire }} />)
  await screen.findByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' })
  fireEvent.click(screen.getByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันยกเลิกคำขอ' }).closest('form')!)
  await waitFor(() => expect(retire).toHaveBeenCalledTimes(1))
  expect((dialog.getByLabelText('ยืนยัน PIN') as HTMLInputElement).value).toBe('')
  expect(JSON.parse(localStorage.getItem(key)!)).toEqual(saved)
  expect(localStorage.getItem(key)).not.toContain('123456')
})

it('uses an already completed result when completion wins the retirement race', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'OpenStore', cashAmount: 500 }
  localStorage.setItem(key, JSON.stringify(saved))
  const closed = { ...state, activeShiftId: null, isClosed: true }
  const read = vi.fn(async () => ({ kind: 'ok' as const, value: closed }))
  const retire = vi.fn(async () => ({ status: 200, body: { status: 'Completed' as const, result: state } }))
  const command = vi.fn()
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: read, storeLifecycleCommand: command, retireStoreLifecycleRequest: retire }} />)
  await screen.findByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' })
  fireEvent.click(screen.getByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันยกเลิกคำขอ' }).closest('form')!)
  await screen.findByRole('button', { name: 'เปิดร้าน' })
  expect(screen.getByText('สาขาบันทึกคำขอเดิมแล้ว')).toBeTruthy()
  expect(screen.getByText('ร้านปิด')).toBeTruthy()
  expect(command).not.toHaveBeenCalled()
  expect(read).toHaveBeenCalledTimes(2)
})

it('preserves original confirmed close totals when retirement discovers a completed close', async () => {
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 490 }
  localStorage.setItem(key, JSON.stringify(saved))
  const completed = { ...state, activeShiftId: null, isClosed: true, closing: {
    storeSessionId: store, shiftId: shift, closesStore: true, expectedCashAmount: 500,
    actualCashAmount: 490, cashDifference: -10, reconciliationStatus: 'Reconciled',
    context: { employeeId: 'E1', deviceId: 'D1', occurredAt: '2026-11-20T00:00:00Z', branchLocalDate: '2026-11-20' },
  } }
  const read = vi.fn(async () => ({ kind: 'ok' as const, value: state }))
  const retire = vi.fn(async () => ({ status: 200, body: { status: 'Completed' as const, result: completed } }))
  const command = vi.fn()
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: read, storeLifecycleCommand: command, retireStoreLifecycleRequest: retire }} />)
  await screen.findByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' })
  fireEvent.click(screen.getByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันยกเลิกคำขอ' }).closest('form')!)
  await screen.findByText('สาขาบันทึกคำขอเดิมแล้ว · ยอดนับจริง 490.00 บาท · ผลต่าง -10.00 บาท')
  expect(localStorage.getItem(key)).toBeNull()
  expect(command).not.toHaveBeenCalled()
})

it('exposes recovery actions from the normal store-and-shift page', async () => {
  const fixture = createOrderingPreview()
  const saved: StoreLifecycleCommand = { requestId: '00000000-0000-4000-8000-000000000003', storeSessionId: store, shiftId: shift, operation: 'CloseStore', cashAmount: 500 }
  localStorage.setItem('pos-store-lifecycle:' + JSON.stringify(fixture.client.scope), JSON.stringify(saved))
  const status = vi.fn(async () => ({ status: 200, body: { status: 'NotFound' as const, result: null } }))
  const client = { ...fixture.client, storeLifecycleCurrent: current, storeLifecycleCommand: vi.fn(),
    storeLifecycleRequestStatus: status, retireStoreLifecycleRequest: vi.fn() }
  render(<App client={client} />)
  fireEvent.click(screen.getByRole('button', { name: 'ร้านและกะ' }))
  await screen.findByRole('button', { name: 'ตรวจสอบผลคำขอ' })
  expect(screen.getByRole('button', { name: 'ยกเลิกคำขอที่ยังไม่บันทึก' })).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบผลคำขอ' }))
  await waitFor(() => expect(status).toHaveBeenCalledWith(saved))
})

it('shows the specific partial-payment table and permits correction after a definite closing rejection', async () => {
  const command = vi.fn(async () => ({ status: 409, body: { error: 'StoreLifecyclePartialPayments',
    tables: [{ tableSessionId: 'SESSION-A', tableId: 'A01' }] } }))
  render(<StoreShiftWorkspace storageKey={key} port={{ storeLifecycleCurrent: current, storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่')
  fireEvent.click(screen.getByRole('button', { name: 'ปิดร้าน' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยอดเงินสดนับจริง'), { target: { value: '500' } })
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดร้าน' }).closest('form')!)
  await screen.findAllByText(/มีรายการชำระบางส่วนที่โต๊ะ A01/)
  expect(localStorage.getItem(key)).toBeNull()
  expect(screen.queryByRole('button', { name: 'ยืนยันคำขอเดิม' })).toBeNull()
  expect((dialog.getByLabelText('ยอดเงินสดนับจริง') as HTMLInputElement).readOnly).toBe(false)
})
