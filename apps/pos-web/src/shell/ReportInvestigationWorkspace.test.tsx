import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { reportDetail, reportItem, reportPage, store, shift } from '../api/closingReportTestFixtures.ts'
import type { ClosingReportDetail, ClosingReportPage, ClosingReportQuery } from '../api/closingReportWorkspaceContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import type { ReportInvestigationTarget } from './reportInvestigation.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const ports = () => ({ closingReports: vi.fn(async (_query: ClosingReportQuery, _signal?: AbortSignal) => ok(reportPage())),
  closingReportDetail: vi.fn(async (_id: string, _signal?: AbortSignal) => ok(reportDetail())) })

it.each([
  ['base-pending', { status: 'Pending', limit: 20 }, 'Pending', 'All', 'รายงานหลัก'],
  ['lifecycle-pending', { status: 'All', lifecycleStatus: 'Pending', limit: 20 }, 'All', 'Pending', 'ข้อมูลเปิดร้านและกะ'],
  ['all', { status: 'All', limit: 20 }, 'All', 'All', null],
] as const)('starts %s with independent filters and a fresh read-only query', async (target, query, base, lifecycle, summary) => {
  const port = ports(), writes = vi.spyOn(Storage.prototype, 'setItem')
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target, revision: 1 }} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls[0]?.[0]).toEqual(query)
  expect((screen.getByLabelText('สถานะรายงานหลัก') as HTMLSelectElement).value).toBe(base)
  expect((screen.getByLabelText('สถานะข้อมูลเปิดร้านและกะ') as HTMLSelectElement).value).toBe(lifecycle)
  if (summary) expect(screen.getByText(new RegExp('กำลังตรวจสอบ' + summary))).toBeTruthy()
  else expect(screen.queryByText(/จำนวนอาจเปลี่ยนจากหน้าสถานะระบบ/)).toBeNull()
  expect(writes).not.toHaveBeenCalled()
  writes.mockRestore()
  const heading = screen.getByRole('heading', { name: 'ตรวจสอบรายงานปิดกะและปิดร้าน' })
  expect(heading.id).toBe('closing-report-workspace-title')
  expect(heading.tabIndex).toBe(-1)
})

it('allows editing and refresh without preserving a stale investigation explanation, and resets to All', async () => {
  const port = ports()
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target: 'lifecycle-pending', revision: 1 }} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  fireEvent.change(screen.getByLabelText('สถานะรายงานหลัก'), { target: { value: 'Delivered' } })
  fireEvent.change(screen.getByLabelText('สถานะข้อมูลเปิดร้านและกะ'), { target: { value: 'NotRecorded' } })
  fireEvent.click(screen.getByRole('button', { name: 'ใช้ตัวกรองรายงาน' }))
  await waitFor(() => expect(port.closingReports).toHaveBeenCalledTimes(2))
  expect(screen.queryByText(/จำนวนอาจเปลี่ยนจากหน้าสถานะระบบ/)).toBeNull()
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบรายการรายงานใหม่' }))
  await waitFor(() => expect(port.closingReports).toHaveBeenCalledTimes(3))
  expect(port.closingReports.mock.calls[2]?.[0]).toEqual({ status: 'Delivered', lifecycleStatus: 'NotRecorded', limit: 20 })
  fireEvent.click(screen.getByRole('button', { name: 'ล้างตัวกรองรายงาน' }))
  await waitFor(() => expect(port.closingReports).toHaveBeenCalledTimes(4))
  expect(port.closingReports.mock.calls[3]?.[0]).toEqual({ status: 'All', limit: 20 })
})

it('new investigation revisions cancel selected detail and do not reveal its late snapshot', async () => {
  const port = ports()
  let finish!: (result: QueryResult<ClosingReportDetail>) => void
  let detailSignal: AbortSignal | undefined
  port.closingReportDetail = vi.fn((_id: string, signal?: AbortSignal) => {
    detailSignal = signal
    return new Promise<QueryResult<ClosingReportDetail>>(resolve => { finish = resolve })
  })
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target: 'base-pending', revision: 1 }} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target: 'base-pending', revision: 2 }} />)
  expect(detailSignal?.aborted).toBe(true)
  await act(async () => finish(ok(reportDetail())))
  expect(screen.queryByRole('region', { name: 'รายงานปิดที่บันทึกไว้' })).toBeNull()
  expect(screen.queryByText('saved-employee')).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'Pending', limit: 20 })
})

it('switching investigation target cancels continuation and starts without its cursor or rows', async () => {
  const port = ports()
  let finish!: (result: QueryResult<ClosingReportPage>) => void
  let pageSignal: AbortSignal | undefined
  port.closingReports = vi.fn().mockResolvedValueOnce(ok(reportPage([reportItem()], 'oldcursor')))
    .mockImplementationOnce((_query: ClosingReportQuery, signal?: AbortSignal) => {
      pageSignal = signal
      return new Promise<QueryResult<ClosingReportPage>>(resolve => { finish = resolve })
    }).mockResolvedValue(ok(reportPage([])))
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target: 'base-pending', revision: 1 }} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' }))
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} investigation={{ target: 'lifecycle-pending', revision: 1 }} />)
  expect(pageSignal?.aborted).toBe(true)
  await act(async () => finish(ok(reportPage([reportItem({ requestId: 'cccccccc-cccc-cccc-cccc-cccccccccccc' })]))))
  await screen.findByText('ไม่พบรายงานตามตัวกรองนี้')
  expect(screen.queryByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', lifecycleStatus: 'Pending', limit: 20 })
})

it('keeps old shift focus compatible and resets scope plus external refresh to the supplied investigation', async () => {
  const port = ports()
  const view = render(<ClosingReportWorkspace port={port} scopeKey="old" refreshKey={0} focus={{ storeSessionId: store, shiftId: shift, revision: 1 }} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls[0]?.[0]).toEqual({ status: 'All', limit: 20, storeSessionId: store, shiftId: shift })
  const target: ReportInvestigationTarget = 'lifecycle-pending'
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="new" refreshKey={1} investigation={{ target, revision: 1 }} />)
  await waitFor(() => expect(port.closingReports).toHaveBeenCalledTimes(2))
  expect(port.closingReports.mock.calls[1]?.[0]).toEqual({ status: 'All', lifecycleStatus: 'Pending', limit: 20 })
  expect((screen.getByLabelText('รอบร้านที่ต้องการค้น') as HTMLInputElement).value).toBe('')
})
