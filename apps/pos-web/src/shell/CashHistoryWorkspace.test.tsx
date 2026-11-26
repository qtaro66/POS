import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import { cashDetail, cashItem, cashPage } from '../api/cashHistoryTestFixtures.ts'
import type { CashHistoryDetail, CashHistoryPage, CashHistoryQuery } from '../api/cashHistoryContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import App from '../App.tsx'
import { createPreviewReadClient } from '../ordering/preview.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function ports() {
  return { cashHistory: vi.fn(async (_query: CashHistoryQuery = {}, _signal?: AbortSignal) => ok(cashPage())),
    cashHistoryDetail: vi.fn(async (_requestId: string, _signal?: AbortSignal) => ok(cashDetail())) }
}
it('uses server effective business date, opens immutable details beside list and exposes no mutation controls', async () => {
  const port = ports(), stored = JSON.stringify(Object.entries(localStorage))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  expect(port.cashHistory).toHaveBeenCalledWith({ limit: 20 }, expect.any(AbortSignal))
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  const detail = within(await screen.findByRole('region', { name: 'รายละเอียดธุรกรรมเงินสด' }))
  expect(await detail.findByText('employee-1')).toBeTruthy()
  expect(detail.getAllByText('110.00 บาท')).toHaveLength(2)
  expect(screen.getByRole('region', { name: 'รายการธุรกรรมเงินสด' })).toBeTruthy()
  expect(screen.getByText(/ไม่ใช่หลักฐานว่าสั่งพิมพ์แล้ว/)).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^(พิมพ์|Refund|คืนเงิน|ยกเลิก|ชำระเงิน|PIN)/ })).toBeNull()
  expect(JSON.stringify(Object.entries(localStorage))).toBe(stored)
})
it('applies canonical search filters, refreshes the applied query and resets to Branch default day', async () => {
  const port = ports()
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  await screen.findByRole('button', { name: /TEST-001/ })
  fireEvent.change(screen.getByLabelText('วันธุรกิจ'), { target: { value: '2026-11-23' } })
  fireEvent.change(screen.getByLabelText('เลขใบเสร็จ'), { target: { value: ' TEST-%_ ' } })
  fireEvent.change(screen.getByLabelText('รหัสรอบโต๊ะ'), { target: { value: ' SESSION-1 ' } })
  fireEvent.change(screen.getByLabelText('ช่องทางชำระ'), { target: { value: 'Cash' } })
  fireEvent.change(screen.getByLabelText('สถานะธุรกรรม'), { target: { value: 'Completed' } })
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหาธุรกรรม' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  const expected = { limit: 20, businessDate: '2026-11-23', receiptNumber: 'TEST-%_', tableSessionId: 'SESSION-1', paymentMethod: 'Cash', status: 'Completed' }
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual(expected)
  fireEvent.click(screen.getByRole('button', { name: 'อ่านประวัติใหม่' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual(expected)
  fireEvent.click(screen.getByRole('button', { name: 'ล้างตัวกรองธุรกรรม' }))
  await screen.findByRole('button', { name: /TEST-001/ })
  expect(port.cashHistory.mock.calls.at(-1)?.[0]).toEqual({ limit: 20 })
  expect((screen.getByLabelText('วันธุรกิจ') as HTMLInputElement).value).toBe('')
})
it('pins effective day across continuation and rejects another day without claiming empty history', async () => {
  const port = ports()
  port.cashHistory.mockResolvedValueOnce(ok(cashPage([cashItem()], 'cursor')))
    .mockResolvedValueOnce(ok({ ...cashPage([cashItem({ requestId: 'other', businessDate: '2026-11-25' })]), businessDate: '2026-11-25' }))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านธุรกรรมเพิ่มเติม' }))
  expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'ยังอ่านหน้าถัดไปไม่ได้ กรุณาค้นหาใหม่')
  expect(port.cashHistory.mock.calls[1][0]).toEqual({ limit: 20, cursor: 'cursor' })
  expect(screen.getByRole('button', { name: /TEST-001/ })).toBeTruthy()
  expect(screen.queryByText('ไม่พบธุรกรรมตามตัวกรองนี้')).toBeNull()
})
it('aborts a delayed continuation when filters change and ignores late rows', async () => {
  const port = ports(); let finish!: (value: QueryResult<CashHistoryPage>) => void; let oldSignal: AbortSignal | undefined
  port.cashHistory.mockResolvedValueOnce(ok(cashPage([cashItem()], 'cursor')))
    .mockImplementationOnce((_, signal) => { oldSignal = signal; return new Promise(resolve => { finish = resolve }) })
    .mockResolvedValue(ok(cashPage([])))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านธุรกรรมเพิ่มเติม' }))
  fireEvent.change(screen.getByLabelText('เลขใบเสร็จ'), { target: { value: 'new' } })
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหาธุรกรรม' }))
  expect(oldSignal?.aborted).toBe(true)
  await screen.findByText('ไม่พบธุรกรรมตามตัวกรองนี้')
  await act(async () => { finish(ok(cashPage([cashItem({ requestId: 'late', receiptNumber: 'LATE-ROW' })]))); await Promise.resolve() })
  expect(screen.queryByText('LATE-ROW')).toBeNull()
})
it('does not display a mismatched detail and preserves historical missing financials as unavailable', async () => {
  const port = ports()
  port.cashHistoryDetail.mockResolvedValueOnce(ok(cashDetail(cashItem({ appliedAmount: 111, changeAmount: 9 }))))
    .mockResolvedValueOnce(ok({ ...cashDetail(), financials: null }))
  const view = render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'ยังอ่านรายละเอียดนี้ไม่ได้ กรุณาค้นหาใหม่')
  expect(screen.queryByText('employee-1')).toBeNull()
  view.rerender(<CashHistoryWorkspace port={port} scopeKey="replacement" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  expect(await screen.findByText(/รายการเดิมไม่มีรายละเอียดส่วนลด/)).toBeTruthy()
  expect(screen.queryByRole('region', { name: 'ยอดจาก snapshot เดิม' })).toBeNull()
})
it('opens history without lifecycle command capabilities and cancels stale authenticated-client results', async () => {
  const first = ports(), second = ports(); let finish!: (value: QueryResult<CashHistoryPage>) => void; let oldSignal: AbortSignal | undefined
  first.cashHistory.mockImplementationOnce((_, signal) => { oldSignal = signal; return new Promise(resolve => { finish = resolve }) })
  const make = (port: ReturnType<typeof ports>) => ({ ...createPreviewReadClient(), scope: { serviceOrigin: 'https://branch.example', branchId: 'branch', deviceId: 'device' }, ...port })
  const view = render(<App client={make(first)} />)
  fireEvent.click(screen.getByRole('button', { name: 'ประวัติธุรกรรม' }))
  await screen.findByText('กำลังอ่านประวัติธุรกรรม')
  view.rerender(<App client={make(second)} />)
  expect(oldSignal?.aborted).toBe(true)
  await screen.findByRole('button', { name: /TEST-001/ })
  await act(async () => { finish(ok(cashPage([cashItem({ receiptNumber: 'OLD-EMPLOYEE' })]))); await Promise.resolve() })
  expect(screen.queryByText('OLD-EMPLOYEE')).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
  expect(screen.queryByRole('region', { name: 'ประวัติรายการชำระเงินสด' })).toBeNull()
})
it('cancels superseded details and ignores a late response for another selected transaction', async () => {
  const port = ports(), second = cashItem({ requestId: 'CASH-2', receiptNumber: 'TEST-002', employeeId: 'employee-2' })
  let finish!: (value: QueryResult<CashHistoryDetail>) => void; let firstSignal: AbortSignal | undefined
  port.cashHistory.mockResolvedValue(ok(cashPage([cashItem(), second])))
  port.cashHistoryDetail.mockImplementationOnce((_, signal) => { firstSignal = signal; return new Promise(resolve => { finish = resolve }) })
    .mockResolvedValueOnce(ok(cashDetail(second)))
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  fireEvent.click(screen.getByRole('button', { name: /TEST-002/ }))
  expect(firstSignal?.aborted).toBe(true)
  await screen.findByText('employee-2')
  await act(async () => { finish(ok(cashDetail())); await Promise.resolve() })
  expect(screen.queryByText('employee-1')).toBeNull()
})
it('clears prior data on failed refresh and never represents an unavailable read as empty history', async () => {
  const port = ports()
  port.cashHistory.mockResolvedValueOnce(ok(cashPage())).mockResolvedValueOnce({ kind: 'unavailable', reason: 'network' })
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  await screen.findByText('employee-1')
  fireEvent.click(screen.getByRole('button', { name: 'อ่านประวัติใหม่' }))
  expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'ยังอ่านประวัติธุรกรรมไม่ได้ กรุณาค้นหาใหม่')
  expect(screen.queryByText('employee-1')).toBeNull()
  expect(screen.queryByText('ไม่พบธุรกรรมตามตัวกรองนี้')).toBeNull()
})
it('aborts a pending detail on screen lock and cannot restore captured data afterward', async () => {
  const port = ports(); let finish!: (value: QueryResult<CashHistoryDetail>) => void; let signal: AbortSignal | undefined
  port.cashHistoryDetail.mockImplementationOnce((_, value) => { signal = value; return new Promise(resolve => { finish = resolve }) })
  render(<App client={{ ...createPreviewReadClient(), scope: { serviceOrigin: 'https://branch.example', branchId: 'branch', deviceId: 'device' }, ...port }} />)
  fireEvent.click(screen.getByRole('button', { name: 'ประวัติธุรกรรม' }))
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  await screen.findByText('กำลังอ่านรายละเอียดธุรกรรม')
  fireEvent.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
  expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(cashDetail())); await Promise.resolve() })
  expect(screen.queryByText('employee-1')).toBeNull()
})
it.each(['preview', 'scope', 'read', 'detail'] as const)('keeps history non-operational when missing capability %s', async missing => {
  const port = ports(), client = { ...createPreviewReadClient(), scope: missing === 'scope' ? undefined : { serviceOrigin: 'https://branch.example', branchId: 'branch', deviceId: 'device' },
    cashHistory: missing === 'read' ? undefined : port.cashHistory, cashHistoryDetail: missing === 'detail' ? undefined : port.cashHistoryDetail }
  render(<App client={client} preview={missing === 'preview'} />)
  fireEvent.click(screen.getByRole('button', { name: 'ประวัติธุรกรรม' }))
  expect(screen.getByText('หน้านี้ยังไม่เชื่อมต่อข้อมูลและคำสั่ง จึงยังไม่พร้อมใช้งาน')).toBeTruthy()
  expect(port.cashHistory).not.toHaveBeenCalled()
})
