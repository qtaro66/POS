import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { QueryResult } from '../api/orderingClient.ts'
import type { ClosingReportDetail, ClosingReportPage, ClosingReportQuery } from '../api/closingReportWorkspaceContracts.ts'
import { branch, now, reportDetail, reportItem, reportPage, shift, store } from '../api/closingReportTestFixtures.ts'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { DataFreshnessPanel } from './DataFreshnessPanel.tsx'
const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const ports = () => ({ closingReports: vi.fn(async (_query?: ClosingReportQuery, _signal?: AbortSignal) => ok(reportPage())), closingReportDetail: vi.fn(async (_requestId: string, _signal?: AbortSignal) => ok(reportDetail())),
  closingReportDelivery: vi.fn(async () => ok({ branchId: branch, asOf: now, deliveryEnabled: false })) })
const nextId = '00000000-0000-0000-0000-000000000004'

it('shows saved cash/financial/table snapshots and original attribution without inferring Central availability', async () => {
  render(<ClosingReportWorkspace port={ports()} scopeKey="branch" refreshKey={0} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(screen.queryByRole('complementary', { name: 'การส่งรายงานอัตโนมัติ' })).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  const detail = within(await screen.findByRole('region', { name: 'รายงานปิดที่บันทึกไว้' }))
  await detail.findByText('saved-employee')
  expect(detail.getByText('490.00 บาท')).toBeTruthy()
  expect(detail.getByText('-10.00 บาท')).toBeTruthy()
  expect(detail.getByText('VAT รวมในราคา')).toBeTruthy()
  expect(detail.getByText('2 โต๊ะ')).toBeTruthy()
  expect(detail.getByText(/ไม่คำนวณใหม่จากยอดปัจจุบัน/)).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^Retry|Reprint|ส่งใหม่|พิมพ์/ })).toBeNull()
})

it('shows uncounted and legacy snapshots as unavailable rather than zero', async () => {
  const port = ports()
  const item = reportItem({ operation: 'CloseShift' }); const saved = reportDetail(item)
  saved.snapshot.cashSummary = null
  Object.assign(saved.snapshot.closing, { actualCashAmount: null, cashDifference: null, reconciliationStatus: 'NotReconciled' })
  port.closingReports.mockResolvedValue(ok(reportPage([item])))
  port.closingReportDetail.mockResolvedValue(ok(saved))
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByText('ไม่ได้กระทบยอด (NotReconciled)')
  expect(screen.getByText(/รายงานเดิมไม่มีสรุปยอดขายเงินสด/)).toBeTruthy()
})

it('paginates with the bound cursor and refreshes without retaining an old selected snapshot', async () => {
  const port = ports()
  port.closingReports.mockResolvedValueOnce(ok(reportPage([reportItem()], 'opaque'))).mockResolvedValueOnce(ok(reportPage([reportItem({ requestId: nextId })])))
    .mockResolvedValue(ok(reportPage()))
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' }))
  await screen.findAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })).toHaveLength(2))
  expect(port.closingReports.mock.calls[1]?.[0]).toMatchObject({ cursor: 'opaque', status: 'All', limit: 20 })
  fireEvent.click(screen.getAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })[0])
  await screen.findByText('saved-employee')
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบรายการรายงานใหม่' }))
  expect(screen.queryByText('saved-employee')).toBeNull()
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
})

it('applies date/status/shift filters and resets pagination, then clears filters', async () => {
  const port = ports()
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  fireEvent.change(screen.getByLabelText('สถานะรายงาน'), { target: { value: 'Pending' } })
  fireEvent.change(screen.getByLabelText('วันธุรกิจของรายงาน'), { target: { value: '2026-11-21' } })
  fireEvent.change(screen.getByLabelText('รอบร้านที่ต้องการค้น'), { target: { value: store } })
  fireEvent.change(screen.getByLabelText('กะที่ต้องการค้น'), { target: { value: shift } })
  fireEvent.click(screen.getByRole('button', { name: 'ใช้ตัวกรองรายงาน' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'Pending', limit: 20, businessDate: '2026-11-21', storeSessionId: store, shiftId: shift })
  fireEvent.click(screen.getByRole('button', { name: 'ล้างตัวกรองรายงาน' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', limit: 20 })
})

it('cancels late list and detail results on scope changes and unmount', async () => {
  const port = ports()
  let resolve!: (value: QueryResult<ClosingReportDetail>) => void
  let detailSignal: AbortSignal | undefined
  port.closingReportDetail = vi.fn((_: string, signal?: AbortSignal) => { detailSignal = signal; return new Promise<QueryResult<ClosingReportDetail>>(finish => { resolve = finish }) }) as typeof port.closingReportDetail
  const view = render(<ClosingReportWorkspace port={port} scopeKey="old" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="new" refreshKey={0} />)
  expect(detailSignal?.aborted).toBe(true)
  await act(async () => resolve(ok(reportDetail())))
  expect(screen.queryByText('saved-employee')).toBeNull()
  view.unmount()
})

it('resets a cancelled pagination request when the reader changes in the same scope', async () => {
  const port = ports()
  let finish!: (value: QueryResult<ClosingReportPage>) => void
  let signal: AbortSignal | undefined
  port.closingReports = vi.fn().mockResolvedValueOnce(ok(reportPage([reportItem()], 'old'))).mockImplementationOnce((_, next?: AbortSignal) => {
    signal = next; return new Promise<QueryResult<ClosingReportPage>>(resolve => { finish = resolve })
  })
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' }))
  const replacement = vi.fn().mockResolvedValueOnce(ok(reportPage([reportItem()], 'new'))).mockResolvedValueOnce(ok(reportPage([reportItem({ requestId: nextId })])))
  view.rerender(<ClosingReportWorkspace port={{ ...port, closingReports: replacement }} scopeKey="branch" refreshKey={0} />)
  expect(signal?.aborted).toBe(true)
  const more = await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' })
  expect((more as HTMLButtonElement).disabled).toBe(false)
  await act(async () => finish(ok(reportPage([reportItem({ requestId: nextId })]))))
  fireEvent.click(more)
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })).toHaveLength(2))
})

it('shows failed and absent readers without claiming an empty queue or blocking store operations', async () => {
  render(<ClosingReportWorkspace port={{}} scopeKey="branch" refreshKey={0} />)
  expect(screen.getByText(/ยังอ่านรายการรายงานไม่ได้/)).toBeTruthy()

  expect(screen.queryByText('ไม่พบรายงานตามตัวกรองนี้')).toBeNull()
})

it('focuses a historical shift and updates after accepted lifecycle refresh', async () => {
  const port = ports()
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} focus={{ storeSessionId: store, shiftId: shift, revision: 1 }} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls[0]?.[0]).toMatchObject({ storeSessionId: store, shiftId: shift })
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={1} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports).toHaveBeenCalledTimes(2)
})

it('shows queue counts and delivery configuration independently on system status and refreshes them', async () => {
  const read = vi.fn(async () => ok({ branchId: branch, pendingCount: 2, leasedCount: 0, deliveredCount: 0, lastDeliveredAt: null }))
  const delivery = vi.fn(async () => ok({ branchId: branch, asOf: now, deliveryEnabled: true }))
  render(<DataFreshnessPanel state={{ synced: { phase: 'unavailable' }, sent: { phase: 'unavailable' } }} onRefresh={() => {}} closingReportPort={{ closingReportQueueStatus: read, closingReportDelivery: delivery }} />)
  await screen.findByText('2 รายงาน')
  expect(await screen.findByText('ตั้งค่าเปิดส่งรายงานอัตโนมัติสำหรับสาขานี้')).toBeTruthy()
  expect(screen.getByText(/การเปิดตัวส่งไม่ได้ยืนยันว่าส่วนกลางพร้อมใช้งาน/)).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'อ่านสถานะใหม่' }))
  await screen.findByText('2 รายงาน')
  expect(read).toHaveBeenCalledTimes(2)
  expect(delivery).toHaveBeenCalledTimes(2)
})
