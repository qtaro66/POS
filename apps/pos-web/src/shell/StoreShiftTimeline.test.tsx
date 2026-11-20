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

it('shows current cash applied totals and receipt count from the normal store page', async () => {
  const fixture = createOrderingPreview()
  const summary = { source: 'CashSales' as const, store: { receiptCount: 3, appliedCashAmount: 360 }, shift: { receiptCount: 1, appliedCashAmount: 200 } }
  const source = async () => ({ kind: 'ok' as const, value: { ...workspace, cashSummary: summary } })
  const client = { ...fixture.client, storeLifecycleCurrent: async () => ({ kind: 'ok' as const, value: state }), storeLifecycleWorkspace: source }
  render(<App client={client} />)
  fireEvent.click(screen.getByRole('button', { name: 'ร้านและกะ' }))
  const live = within(await screen.findByRole('region', { name: 'สรุปเงินสดที่บันทึกล่าสุด' }))
  const currentShift = within(live.getByRole('region', { name: 'เงินสดของกะปัจจุบัน' }))
  expect(currentShift.getByText('1 ใบ')).toBeTruthy()
  expect(currentShift.getByText('200.00 บาท')).toBeTruthy()
  expect(currentShift.queryByText('300.00 บาท')).toBeNull()
  const currentStore = within(live.getByRole('region', { name: 'เงินสดสะสมรอบร้าน' }))
  expect(currentStore.getByText('3 ใบ')).toBeTruthy()
  expect(currentStore.getByText('360.00 บาท')).toBeTruthy()
})

it('keeps a frozen closing cash summary separate from later live totals', async () => {
  const frozen = { source: 'CashSales' as const, store: { receiptCount: 1, appliedCashAmount: 100 }, shift: { receiptCount: 1, appliedCashAmount: 100 } }
  const live = { source: 'CashSales' as const, store: { receiptCount: 2, appliedCashAmount: 300 }, shift: { receiptCount: 1, appliedCashAmount: 200 } }
  const source = async () => ({ kind: 'ok' as const, value: { ...workspace, cashSummary: live } })
  const closing = vi.fn(async () => ({ kind: 'ok' as const, value: { ...first, cashSummary: frozen } }))
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: source, storeShiftClosing: closing }} sourceState={state} />)
  await screen.findByRole('region', { name: 'สรุปเงินสดที่บันทึกล่าสุด' })
  fireEvent.click(screen.getByRole('button', { name: 'ดูผลปิดกะ' }))
  const recorded = within(await screen.findByRole('region', { name: 'สรุปเงินสดที่บันทึก ณ เวลาปิด' }))
  expect(recorded.getAllByText('100.00 บาท')).toHaveLength(2)
  expect(recorded.queryByText('300.00 บาท')).toBeNull()
  const dialog = within(screen.getByRole('dialog'))
  expect(dialog.getByText('ไม่ได้กระทบยอด (NotReconciled)')).toBeTruthy()
})

it('shows legacy missing captured cash totals as unavailable and never substitutes live cash totals', async () => {
  const source = async () => ({ kind: 'ok' as const, value: { ...workspace, cashSummary: {
    source: 'CashSales' as const, store: { receiptCount: 2, appliedCashAmount: 300 }, shift: null,
  } } })
  const closing = vi.fn(async () => ({ kind: 'ok' as const, value: { ...first, cashSummary: null } }))
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: source, storeShiftClosing: closing }} sourceState={state} />)
  await screen.findByRole('button', { name: 'ดูผลปิดกะ' })
  fireEvent.click(screen.getByRole('button', { name: 'ดูผลปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  await dialog.findByText('ผลปิดกะนี้ไม่มีสรุปยอดขายเงินสดที่บันทึกไว้')
  expect(dialog.queryByText('300.00 บาท')).toBeNull()
  expect(dialog.queryByText('0.00 บาท')).toBeNull()
})

it('invalidates live cash totals while refreshing and keeps them hidden on a failed response', async () => {
  let finish!: (value: { kind: 'unavailable'; reason: 'protocol' }) => void
  const source = vi.fn()
    .mockResolvedValueOnce({ kind: 'ok', value: { ...workspace, cashSummary: {
      source: 'CashSales', store: { receiptCount: 1, appliedCashAmount: 999 }, shift: null,
    } } })
    .mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: source }} sourceState={state} />)
  await screen.findByText('999.00 บาท')
  fireEvent.click(screen.getByRole('button', { name: 'อ่านประวัติกะอีกครั้ง' }))
  await screen.findByText('กำลังอ่านประวัติกะ')
  expect(screen.queryByText('999.00 บาท')).toBeNull()
  await act(async () => finish({ kind: 'unavailable', reason: 'protocol' }))
  await screen.findByText('ยังอ่านประวัติกะไม่ได้')
  expect(screen.queryByText('999.00 บาท')).toBeNull()
  expect(screen.queryByText('0.00 บาท')).toBeNull()
})

it('associates live cash totals with the actual older open-store Business Date after rollover', async () => {
  const old = { ...state, businessDate: '2026-11-19' }
  const source = async () => ({ kind: 'ok' as const, value: { ...workspace, current: old, stores: [], cashSummary: {
    source: 'CashSales' as const, store: { receiptCount: 1, appliedCashAmount: 900 }, shift: { receiptCount: 1, appliedCashAmount: 900 },
  } } })
  render(<StoreShiftTimeline port={{ storeLifecycleWorkspace: source }} sourceState={old} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 0 กะ')
  const summary = within(screen.getByRole('region', { name: 'สรุปเงินสดที่บันทึกล่าสุด' }))
  expect(summary.getByText('รอบร้าน S1 · Business Date 2026-11-19')).toBeTruthy()
  expect(summary.getAllByText('900.00 บาท')).toHaveLength(2)
  expect(summary.queryByText(/Business Date 2026-11-20/)).toBeNull()
})

const financials = {
  itemGrossAmount: 120, lineDiscountAmount: 10, billDiscountAmount: 5, serviceChargeAmount: 5.9,
  totalBeforeRounding: 110.9, roundingAdjustment: -0.9, amountDue: 110, vatInclusiveAmount: 7.26, amountBeforeVat: 103.64,
}

it('shows recorded bill tax, discounts and rounding separately from newer live cash totals', async () => {
  const captured = { ...first, cashSummary: { source: 'CashSales' as const,
    store: { receiptCount: 1, appliedCashAmount: 110, financials },
    shift: { receiptCount: 1, appliedCashAmount: 110, financials } } }
  const live = { ...workspace, cashSummary: { source: 'CashSales' as const,
    store: { receiptCount: 2, appliedCashAmount: 220 }, shift: { receiptCount: 1, appliedCashAmount: 110 } } }
  render(<StoreShiftTimeline sourceState={state} port={{
    storeLifecycleWorkspace: async () => ({ kind: 'ok', value: live }),
    storeShiftClosing: async () => ({ kind: 'ok', value: captured }),
  }} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 2 กะ')
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูผลปิดกะ' })[0])
  const dialog = within(screen.getByRole('dialog'))
  await dialog.findByText('ยอดขายเงินสด ณ เวลาปิด')
  const shift = within(dialog.getByRole('region', { name: 'เงินสดของกะที่ปิด' }))
  fireEvent.click(shift.getByText('รายละเอียดบิลเงินสด'))
  expect(shift.getByText('7.26 บาท')).toBeTruthy()
  expect(shift.getByText('-0.90 บาท')).toBeTruthy()
  expect(shift.getByText('ส่วนลดท้ายบิล')).toBeTruthy()
  expect(dialog.queryByText('220.00 บาท')).toBeNull()
})

it('labels older captured cash data without complete financial fields as unavailable', async () => {
  const saved = { ...first, cashSummary: { source: 'CashSales' as const,
    store: { receiptCount: 1, appliedCashAmount: 100 }, shift: { receiptCount: 1, appliedCashAmount: 100 } } }
  render(<StoreShiftTimeline sourceState={state} port={{
    storeLifecycleWorkspace: read,
    storeShiftClosing: async () => ({ kind: 'ok', value: saved }),
  }} />)
  await screen.findByText('Business Date 2026-11-20 · จำนวนกะ 2 กะ')
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูผลปิดกะ' })[0])
  const dialog = within(screen.getByRole('dialog'))
  await dialog.findByText('ยอดขายเงินสด ณ เวลาปิด')
  expect(dialog.getAllByText('ยังไม่มีรายละเอียดภาษีและส่วนลดที่บันทึกไว้ครบ')).toHaveLength(2)
  expect(dialog.queryByText('รายละเอียดบิลเงินสด')).toBeNull()
})

it('shows live table state separately from the immutable carryover counts at closing', async () => {
  const live = { ...workspace, tables: { openTableCount: 3, awaitingPaymentTableCount: 2, partiallyPaidTableCount: 1 } }
  const saved = { ...first, cashSummary: { source: 'CashSales' as const,
    store: { receiptCount: 0, appliedCashAmount: 0 }, shift: null,
    tables: { openTableCount: 2, awaitingPaymentTableCount: 1, partiallyPaidTableCount: 0 } } }
  render(<StoreShiftTimeline sourceState={state} port={{
    storeLifecycleWorkspace: async () => ({ kind: 'ok', value: live }),
    storeShiftClosing: async () => ({ kind: 'ok', value: saved }),
  }} />)
  const currentTables = within(await screen.findByRole('region', { name: 'สถานะโต๊ะล่าสุด' }))
  expect(currentTables.getByText('3 โต๊ะ')).toBeTruthy()
  expect(currentTables.getByText('1 โต๊ะ')).toBeTruthy()
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูผลปิดกะ' })[0])
  const dialog = within(screen.getByRole('dialog'))
  const recorded = within(await dialog.findByRole('region', { name: 'โต๊ะ ณ เวลาปิด' }))
  expect(recorded.getByText('2 โต๊ะ')).toBeTruthy()
  expect(recorded.getByText('0 โต๊ะ')).toBeTruthy()
  expect(recorded.queryByText('3 โต๊ะ')).toBeNull()
})
