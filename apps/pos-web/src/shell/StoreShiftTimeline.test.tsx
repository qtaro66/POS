import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { beforeAll, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import { StoreShiftTimeline } from './StoreShiftTimeline.tsx'
import type { StoreLifecycleState, StoreWorkspace, StoreShiftEntry } from '../api/storeLifecycleContracts.ts'

const context = (employeeId: string, occurredAt: string) => ({ employeeId, deviceId: 'D1', occurredAt, branchLocalDate: '2026-11-20' })
const first: StoreShiftEntry = {
  storeSessionId: 'S1', shiftId: 'H1', openingCashAmount: 500, opened: context('OPENER-1', '2026-11-20T01:00:00Z'),
  closing: { storeSessionId: 'S1', shiftId: 'H1', closesStore: false, expectedCashAmount: 600, actualCashAmount: null,
    cashDifference: null, reconciliationStatus: 'NotReconciled', context: context('CLOSER-1', '2026-11-20T02:00:00Z') },
}
const second: StoreShiftEntry = { storeSessionId: 'S1', shiftId: 'H2', openingCashAmount: null, opened: context('OPENER-2', '2026-11-20T02:00:00Z'), closing: null }
const state: StoreLifecycleState = { storeSessionId: 'S1', activeShiftId: 'H2', isClosed: false, businessDate: '2026-11-20', openingCashAmount: 500, closing: first.closing }
const workspace: StoreWorkspace = { branchId: 'B1', businessDate: '2026-11-20', asOf: '2026-11-20T04:00:00Z', current: state,
  stores: [{ storeSessionId: 'S1', businessDate: '2026-11-20', openingCashAmount: 500, opened: first.opened, isClosed: false, shifts: [first, second] }] }
beforeAll(() => { HTMLDialogElement.prototype.showModal = function () { this.open = true } })
const read = async () => ({ kind: 'ok' as const, value: workspace })

it('shows shift count, opening attribution, Bangkok time and duration from Branch observation', async () => {
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: read }} sourceState={state} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 2 กะ')
  expect(screen.getByText('OPENER-1')).toBeTruthy()
  expect(screen.getByText('OPENER-2')).toBeTruthy()
  expect(screen.getByText('2 ชม. 0 นาที')).toBeTruthy()
  const opened = screen.getAllByText(/8:00/)[0]
  expect(opened.getAttribute('datetime')).toBe('2026-11-20T01:00:00Z')
  const entries = screen.getAllByRole('listitem')
  expect(entries[0].textContent).toContain('H1')
  expect(entries[1].textContent).toContain('H2')
  expect(screen.queryByLabelText('ยืนยัน PIN')).toBeNull()
})

it('reads the immutable nullable close snapshot without PIN or new lifecycle commands', async () => {
  const closing = vi.fn(async () => ({ kind: 'ok' as const, value: first }))
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: read, storeShiftClosing: closing }} sourceState={state} />)
  await screen.findByRole('button', { name: 'ดูผลปิดกะ' })
  fireEvent.click(screen.getByRole('button', { name: 'ดูผลปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  await dialog.findByText('ไม่ได้กระทบยอด (NotReconciled)')
  expect(dialog.getByText('600.00 บาท')).toBeTruthy()
  expect(dialog.queryByLabelText('ยืนยัน PIN')).toBeNull()
  expect(closing).toHaveBeenCalledWith('S1', 'H1', expect.any(AbortSignal))
})

it('ignores a late closing lookup after the selected shift changes', async () => {
  let finishFirst!: (result: { kind: 'ok'; value: StoreShiftEntry }) => void
  const late = new Promise<{ kind: 'ok'; value: StoreShiftEntry }>(resolve => { finishFirst = resolve })
  const closedSecond = { ...second, closing: { ...first.closing!, shiftId: 'H2', actualCashAmount: 700, cashDifference: 100, reconciliationStatus: 'Reconciled' } }
  const readClosed = async () => ({ kind: 'ok' as const, value: { ...workspace, stores: [{ ...workspace.stores[0], shifts: [first, closedSecond] }] } })
  const closing = vi.fn(async (_store: string, shift: string) => shift === 'H1' ? late : { kind: 'ok' as const, value: closedSecond })
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: readClosed, storeShiftClosing: closing }} sourceState={state} />)
  await screen.findAllByRole('button', { name: 'ดูผลปิดกะ' })
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูผลปิดกะ' })[0])
  await screen.findByText('กำลังอ่านผลปิดกะ')
  fireEvent.click(screen.getByRole('button', { name: 'ปิดผลปิดกะ' }))
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูผลปิดกะ' })[1])
  await within(screen.getByRole('dialog')).findByText('700.00 บาท')
  await act(async () => finishFirst({ kind: 'ok', value: first }))
  const dialog = within(screen.getByRole('dialog'))
  expect(dialog.getByText('700.00 บาท')).toBeTruthy()
  expect(dialog.queryByText('ไม่ได้กระทบยอด (NotReconciled)')).toBeNull()
})

it('cancels reads on unmount and keeps an older Business Date separate from today', async () => {
  let finish!: (result: { kind: 'ok'; value: StoreWorkspace }) => void
  let signal: AbortSignal | undefined
  const waiting = vi.fn((abort?: AbortSignal) => {
    signal = abort
    return new Promise<{ kind: 'ok'; value: StoreWorkspace }>(resolve => { finish = resolve })
  })
  const view = render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: waiting }} sourceState={state} />)
  view.unmount()
  expect(signal!.aborted).toBe(true)
  await act(async () => finish({ kind: 'ok', value: workspace }))
  const old = { ...state, businessDate: '2026-11-19' }
  const today = async () => ({ kind: 'ok' as const, value: { ...workspace, current: old, stores: [] } })
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: today }} sourceState={old} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 0 กะ')
  expect(screen.getByText('ยังไม่มีกะในวันธุรกิจนี้')).toBeTruthy()
  expect(screen.queryByText('OPENER-1')).toBeNull()
})

it('is wired through the normal store page', async () => {
  const fixture = createOrderingPreview()
  const client = { ...fixture.client, storeLifecycleCurrent: async () => ({ kind: 'ok' as const, value: state }), storeLifecycleWorkspace: read }
  render(<App client={client} />)
  fireEvent.click(screen.getByRole('button', { name: 'ร้านและกะ' }))
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 2 กะ')
  expect(screen.getByText('ผลปิดกะล่าสุด')).toBeTruthy()
})

it('retains Branch ordering after a backward wall clock change and clamps elapsed duration', async () => {
  const earlierClose = { ...first, closing: { ...first.closing!, context: context('CLOSER-1', '2026-11-20T00:59:00Z') } }
  const source = async () => ({ kind: 'ok' as const, value: { ...workspace, stores: [{ ...workspace.stores[0], shifts: [earlierClose, second] }] } })
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: source }} sourceState={state} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 2 กะ')
  const rows = screen.getAllByRole('listitem')
  expect(rows[0].textContent).toContain('H1')
  expect(rows[0].textContent).toContain('0 ชม. 0 นาที')
  expect(rows[1].textContent).toContain('H2')
})

it('does not show empty financial values when workspace loading fails', async () => {
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: async () => ({ kind: 'unavailable' as const, reason: 'network' as const }) }} sourceState={state} />)
  await screen.findByText('ยังอ่านประวัติกะไม่ได้')
  expect(screen.queryByText(/จำนวนกะ 0/)).toBeNull()
  expect(screen.queryByRole('listitem')).toBeNull()
})
