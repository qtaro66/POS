import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportQueue } from './ClosingReportQueue.tsx'
import type { ClosingReportQueueStatus } from '../api/closingReportContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const queue: ClosingReportQueueStatus = { branchId: 'BRANCH', pendingCount: 3, leasedCount: 1, deliveredCount: 2, lastDeliveredAt: '2026-11-20T03:00:00Z' }
const ok = (value = queue): QueryResult<ClosingReportQueueStatus> => ({ kind: 'ok', value })
const label = 'รายงานปิดกะและปิดร้านรอส่ง'

it('shows pending including leased, confirmed totals and exact confirmation time without claiming full sync', async () => {
  render(<ClosingReportQueue read={async () => ok()} scopeKey="branch" refreshKey={0} />)
  const panel = within(screen.getByRole('region', { name: label }))
  await panel.findByText('3 รายงาน')
  expect(panel.getByText('1 รายงาน')).toBeTruthy()
  expect(panel.getByText('2 รายงาน')).toBeTruthy()
  expect(panel.getByText('รอการยืนยันจากส่วนกลาง (รวมกำลังส่ง)')).toBeTruthy()
  expect(screen.getByRole('region', { name: label }).querySelector('time')?.dateTime).toBe(queue.lastDeliveredAt)
  expect(panel.getByText(/คิวว่างไม่ได้ยืนยันว่าส่วนกลางพร้อมใช้งาน/)).toBeTruthy()
})

it('clears old counts during refresh and shows unavailable instead of stale success on failure', async () => {
  let finish!: (value: QueryResult<ClosingReportQueueStatus>) => void
  const read = vi.fn().mockResolvedValueOnce(ok()).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
  render(<ClosingReportQueue read={read} scopeKey="branch" refreshKey={0} />)
  await screen.findByText('3 รายงาน')
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบคิวรายงาน' }))
  expect(screen.queryByText('3 รายงาน')).toBeNull()
  expect(screen.getByText('กำลังอ่านคิวรายงาน')).toBeTruthy()
  await act(async () => finish({ kind: 'unavailable', reason: 'network' }))
  expect(screen.getByText(/ยังอ่านคิวรายงานไม่ได้/)).toBeTruthy()
  expect(screen.queryByText('2 รายงาน')).toBeNull()
})

it('drops late results after scope changes and hides previous-branch counts immediately', async () => {
  let finish!: (value: QueryResult<ClosingReportQueueStatus>) => void
  const read = vi.fn().mockResolvedValueOnce(ok()).mockImplementationOnce(() => new Promise(resolve => { finish = resolve })).mockResolvedValueOnce(ok({ ...queue, branchId: 'NEXT', pendingCount: 8 }))
  const view = render(<ClosingReportQueue read={read} scopeKey="old" refreshKey={0} />)
  await screen.findByText('3 รายงาน')
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบคิวรายงาน' }))
  view.rerender(<ClosingReportQueue read={read} scopeKey="next" refreshKey={0} />)
  await screen.findByText('8 รายงาน')
  await act(async () => finish(ok()))
  expect(screen.queryByText('3 รายงาน')).toBeNull()
  expect(screen.getByText('8 รายงาน')).toBeTruthy()
})

it('ignores late responses on unmount and recovers from exceptions through manual refresh', async () => {
  let finish!: (value: QueryResult<ClosingReportQueueStatus>) => void
  let signal: AbortSignal | undefined
  const first = vi.fn((next?: AbortSignal) => { signal = next; return new Promise<QueryResult<ClosingReportQueueStatus>>(resolve => { finish = resolve }) })
  const view = render(<ClosingReportQueue read={first} scopeKey="branch" refreshKey={0} />)
  view.unmount()
  expect(signal?.aborted).toBe(true)
  await act(async () => finish(ok()))
  const read = vi.fn().mockRejectedValueOnce(new Error('offline')).mockResolvedValueOnce(ok({ ...queue, pendingCount: 0, leasedCount: 0, deliveredCount: 0, lastDeliveredAt: null }))
  render(<ClosingReportQueue read={read} scopeKey="branch" refreshKey={0} />)
  await screen.findByText(/ยังอ่านคิวรายงานไม่ได้/)
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบคิวรายงาน' }))
  await screen.findByText('ยังไม่มีการยืนยัน')
  expect(screen.getAllByText('0 รายงาน')).toHaveLength(3)
})

it('refreshes after a completed lifecycle operation without requiring a queue reader', async () => {
  const read = vi.fn().mockResolvedValueOnce(ok()).mockResolvedValueOnce(ok({ ...queue, pendingCount: 4 }))
  const view = render(<ClosingReportQueue read={read} scopeKey="branch" refreshKey={0} />)
  await screen.findByText('3 รายงาน')
  view.rerender(<ClosingReportQueue read={read} scopeKey="branch" refreshKey={1} />)
  await screen.findByText('4 รายงาน')
  view.rerender(<ClosingReportQueue read={undefined} scopeKey="branch" refreshKey={1} />)
  expect(screen.queryByText('4 รายงาน')).toBeNull()
  expect(screen.getByText(/ยังอ่านคิวรายงานไม่ได้/)).toBeTruthy()
})
