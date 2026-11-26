import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import { cashDetail, cashItem, cashItemizedDetail, cashPage } from '../api/cashHistoryTestFixtures.ts'
import type { CashHistoryDetail, CashHistoryPage, CashHistoryQuery } from '../api/cashHistoryContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function ports() {
  const value = cashItemizedDetail()
  return { cashHistory: vi.fn(async (_query: CashHistoryQuery = {}, _signal?: AbortSignal) => ok(cashPage([structuredClone(value.transaction)]))),
    cashHistoryDetail: vi.fn(async (_id: string, _signal?: AbortSignal) => ok(structuredClone(value))) }
}
it('shows captured item names, options, billed quantity, comments, discounts and versions beside the original cash facts', async () => {
  const port = ports(), stored = JSON.stringify(Object.entries(localStorage))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  const bill = within(await screen.findByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' }))
  expect(bill.getByRole('heading', { name: /ข้าวเดิม\s+ชื่อที่บันทึก/ })).toBeTruthy()
  expect(bill.getByText(/จำนวนที่คิดเงิน 2/)).toBeTruthy()
  expect(bill.getByText(/กลับบ้าน/)).toBeTruthy()
  expect(bill.getByText(/ไข่เดิม ×1 ต่อหน่วย/)).toBeTruthy()
  expect(bill.getByText(/ไม่เผ็ด แยกน้ำ/)).toBeTruthy()
  expect(bill.getByText(/ส่วนลดท้ายบิลเดิม/).textContent).toContain('5.00 บาท')
  expect(bill.getByText(/menu-original/).textContent).toContain('policy-original')
  expect(bill.getByText(/คำขอคิดเงินเดิม PREPARE-1/)).toBeTruthy()
  expect(screen.getByText('รหัสโต๊ะเดิม A01')).toBeTruthy()
  expect(screen.getByText('ชุดออเดอร์เดิม ORDER-1')).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^(ยกเลิก|คืนเงิน|พิมพ์|แก้ไข|ลบ|PIN)/ })).toBeNull()
  expect(JSON.stringify(Object.entries(localStorage))).toBe(stored)
})
it('preserves captured fractional unit prices beside their historically rounded gross amount', async () => {
  const value = cashItemizedDetail()
  value.transaction = { ...value.transaction, tenderedAmount: 1, appliedAmount: 1, changeAmount: 0 }
  value.bill!.vatRate = 0; value.bill!.serviceChargeRate = 0; value.bill!.billDiscount = null
  value.bill!.items[0] = { ...value.bill!.items[0], quantity: 1, unitPrice: 1.005, options: [], grossAmount: 1.01, netAmount: 1.01, discount: null }
  value.financials = { itemGrossAmount: 1.01, lineDiscountAmount: 0, billDiscountAmount: 0, serviceChargeAmount: 0, totalBeforeRounding: 1.01, roundingAdjustment: -.01, amountDue: 1, vatInclusiveAmount: 0, amountBeforeVat: 1.01 }
  const port = ports(); port.cashHistory.mockResolvedValue(ok(cashPage([value.transaction]))); port.cashHistoryDetail.mockResolvedValue(ok(value))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  const bill = within(await screen.findByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' }))
  expect(bill.getByText(/ราคาต่อหน่วยที่บันทึก 1\.005 บาท/)).toBeTruthy()
  expect(bill.getByText(/ยอดก่อนส่วนลดที่บันทึก 1\.01 บาท/)).toBeTruthy()
})
it('keeps exact recorded table and order filters on continuation and refresh, then resets them', async () => {
  const port = ports(), second = cashItem({ requestId: 'CASH-2', receiptNumber: 'TEST-002', tableId: 'A01', orderBatchIds: ['ORDER-1'] })
  port.cashHistory.mockResolvedValue(ok(cashPage([cashItemizedDetail().transaction], 'cursor2')))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  await screen.findByRole('button', { name: /TEST-001/ })
  fireEvent.change(screen.getByLabelText('รหัสโต๊ะ ณ คิดเงิน'), { target: { value: ' A01 ' } })
  fireEvent.change(screen.getByLabelText('รหัสชุดออเดอร์เดิม'), { target: { value: ' ORDER-1 ' } })
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหาธุรกรรม' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  const expected = { limit: 20, paymentMethod: 'All', status: 'All', tableId: 'A01', orderBatchId: 'ORDER-1' }
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual(expected)
  port.cashHistory.mockResolvedValueOnce(ok(cashPage([second])))
  fireEvent.click(screen.getByRole('button', { name: 'อ่านธุรกรรมเพิ่มเติม' }))
  await screen.findByRole('button', { name: /TEST-002/ })
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual({ ...expected, cursor: 'cursor2' })
  fireEvent.click(screen.getByRole('button', { name: 'อ่านประวัติใหม่' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual(expected)
  fireEvent.click(screen.getByRole('button', { name: 'ล้างตัวกรองธุรกรรม' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  expect((screen.getByLabelText('รหัสโต๊ะ ณ คิดเงิน') as HTMLInputElement).value).toBe('')
  expect((screen.getByLabelText('รหัสชุดออเดอร์เดิม') as HTMLInputElement).value).toBe('')
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual({ limit: 20 })
})
it.each([undefined, null])('keeps old unavailable itemized data distinct from actual empty items: %s', async bill => {
  const value = { ...cashDetail(cashItem({ tableId: null, orderBatchIds: null })), ...(bill === null ? { bill: null } : {}) }
  const port = ports(); port.cashHistory.mockResolvedValue(ok(cashPage([value.transaction]))); port.cashHistoryDetail.mockResolvedValue(ok(value))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  expect(await screen.findByText(bill === null ? /รายการเดิมไม่ได้บันทึกรายละเอียดสินค้าที่อ่านได้/ : /ยังไม่ทราบรายละเอียดสินค้าเดิมจากบริการสาขารุ่นนี้/)).toBeTruthy()
  expect(screen.queryByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' })).toBeNull()
  expect(screen.queryByText(/ใบคิดเงินเดิมบันทึกรายการสินค้าเป็นรายการว่าง/)).toBeNull()
})
it('aborts old itemized detail when an original-table search replaces the selected view', async () => {
  const port = ports(); let finish!: (value: QueryResult<CashHistoryDetail>) => void; let signal: AbortSignal | undefined
  port.cashHistoryDetail.mockImplementationOnce((_, current) => { signal = current; return new Promise(resolve => { finish = resolve }) })
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  fireEvent.change(screen.getByLabelText('รหัสโต๊ะ ณ คิดเงิน'), { target: { value: 'A02' } })
  port.cashHistory.mockResolvedValue(ok(cashPage([])))
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหาธุรกรรม' }))
  await screen.findByText('ไม่พบธุรกรรมตามตัวกรองนี้')
  expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(cashItemizedDetail())); await Promise.resolve() })
  expect(screen.queryByText(/ข้าวเดิม/)).toBeNull()
  expect(screen.queryByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' })).toBeNull()
})
it('aborts an old continuation before replacing the order-batch filter', async () => {
  const port = ports(); let finish!: (value: QueryResult<CashHistoryPage>) => void; let signal: AbortSignal | undefined
  port.cashHistory.mockResolvedValueOnce(ok(cashPage([cashItemizedDetail().transaction], 'cursor2')))
    .mockImplementationOnce((_, value) => { signal = value; return new Promise(resolve => { finish = resolve }) })
    .mockResolvedValue(ok(cashPage([])))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านธุรกรรมเพิ่มเติม' }))
  fireEvent.change(screen.getByLabelText('รหัสชุดออเดอร์เดิม'), { target: { value: 'OTHER-ORDER' } })
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหาธุรกรรม' }))
  await screen.findByText('ไม่พบธุรกรรมตามตัวกรองนี้'); expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(cashPage([cashItem({ receiptNumber: 'STALE-ITEMS' })]))); await Promise.resolve() })
  expect(screen.queryByText('STALE-ITEMS')).toBeNull()
})
