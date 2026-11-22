import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { reportDetail, reportItem, reportPage } from '../api/closingReportTestFixtures.ts'
import type { ClosingReportDetail, ClosingReportItem, ClosingReportPage, ClosingReportQuery } from '../api/closingReportWorkspaceContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const nextId = 'cccccccc-cccc-cccc-cccc-cccccccccccc'
const ports = () => ({ closingReports: vi.fn(async (query: ClosingReportQuery, _signal?: AbortSignal) => ok(reportPage([reportItem(query.lifecycleStatus === 'NotRecorded' ? { lifecycleDelivery: null } : {})]))), closingReportDetail: vi.fn(async (_requestId: string, _signal?: AbortSignal) => ok(reportDetail())) })
const withPendingContext = (item: ClosingReportItem): ClosingReportItem => ({ ...item, lifecycleDelivery: {
  contextId: 'lifecycle:' + item.reportId, deliveryState: 'Queued', attemptCount: 0,
  availableAt: item.availableAt, leaseExpiresAt: null, deliveredAt: null, lastFailure: null } })
const apply = (value: string) => {
  fireEvent.change(screen.getByLabelText('สถานะข้อมูลเปิดร้านและกะ'), { target: { value } })
  fireEvent.click(screen.getByRole('button', { name: 'ใช้ตัวกรองรายงาน' }))
}

it('combines parent receipt and context filters and carries context scope through continuation and refresh', async () => {
  const port = ports()
  const accepted = withPendingContext(reportItem({ deliveryState: 'Delivered', deliveredAt: '2026-11-21T03:00:00Z', attemptCount: 1 }))
  port.closingReports.mockResolvedValueOnce(ok(reportPage()))
    .mockResolvedValueOnce(ok(reportPage([accepted], 'boundcontext')))
    .mockResolvedValueOnce(ok(reportPage([withPendingContext(reportItem({ requestId: nextId, deliveryState: 'Delivered', deliveredAt: '2026-11-21T03:00:00Z', attemptCount: 1 }))])))
    .mockResolvedValue(ok(reportPage([accepted])))
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  fireEvent.change(screen.getByLabelText('สถานะรายงานหลัก'), { target: { value: 'Delivered' } })
  apply('Pending')
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' }))
  await waitFor(() => expect(screen.getAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })).toHaveLength(2))
  expect(port.closingReports.mock.calls[1]?.[0]).toEqual({ status: 'Delivered', lifecycleStatus: 'Pending', limit: 20 })
  expect(port.closingReports.mock.calls[2]?.[0]).toEqual({ status: 'Delivered', lifecycleStatus: 'Pending', limit: 20, cursor: 'boundcontext' })
  expect(screen.getByText(/การยืนยันรายงานหลักไม่ได้หมายความว่า/)).toBeTruthy()
  expect(screen.getAllByText('สถานะการส่งรายงานหลัก')).toHaveLength(2)
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบรายการรายงานใหม่' }))
  await waitFor(() => expect(port.closingReports).toHaveBeenCalledTimes(4))
  expect(port.closingReports.mock.calls[3]?.[0]).toEqual({ status: 'Delivered', lifecycleStatus: 'Pending', limit: 20 })
})

it('distinguishes historical missing context from pending work and resets both independent filters', async () => {
  const port = ports()
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  apply('NotRecorded')
  expect(await screen.findByText(/กำลังค้นหารายงานที่ไม่มีข้อมูลเปิดร้านและกะบันทึกไว้/)).toBeTruthy()
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  expect(await screen.findByText('ยังไม่ทราบสถานะส่งข้อมูลเปิดร้านและกะ')).toBeTruthy()
  expect(screen.queryByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', lifecycleStatus: 'NotRecorded', limit: 20 })
  fireEvent.change(screen.getByLabelText('สถานะรายงานหลัก'), { target: { value: 'Pending' } })
  fireEvent.click(screen.getByRole('button', { name: 'ล้างตัวกรองรายงาน' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect((screen.getByLabelText('สถานะรายงานหลัก') as HTMLSelectElement).value).toBe('All')
  expect((screen.getByLabelText('สถานะข้อมูลเปิดร้านและกะ') as HTMLSelectElement).value).toBe('All')
  expect(screen.queryByText(/ไม่ถือว่าได้รับการยืนยันหรือเป็นงานค้างส่ง/)).toBeNull()
  expect(screen.queryByText('saved-employee')).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', limit: 20 })
})

it('cancels an old continuation when lifecycle filter changes and rejects its late rows', async () => {
  const port = ports()
  let finish!: (value: QueryResult<ClosingReportPage>) => void
  let signal: AbortSignal | undefined
  port.closingReports = vi.fn().mockResolvedValueOnce(ok(reportPage([reportItem()], 'oldcursor')))
    .mockImplementationOnce((_, next?: AbortSignal) => { signal = next; return new Promise<QueryResult<ClosingReportPage>>(resolve => { finish = resolve }) })
    .mockResolvedValue(ok(reportPage([])))
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านรายงานเพิ่มเติม' }))
  apply('Delivered')
  expect(signal?.aborted).toBe(true)
  await screen.findByText('ไม่พบรายงานตามตัวกรองนี้')
  await act(async () => finish(ok(reportPage([reportItem({ requestId: nextId })]))))
  expect(screen.queryByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', lifecycleStatus: 'Delivered', limit: 20 })
})

it('cancels a selected report detail when context filter changes without retaining its late snapshot', async () => {
  const port = ports()
  let finish!: (value: QueryResult<ClosingReportDetail>) => void
  let signal: AbortSignal | undefined
  port.closingReportDetail = vi.fn((_: string, next?: AbortSignal) => {
    signal = next
    return new Promise<QueryResult<ClosingReportDetail>>(resolve => { finish = resolve })
  }) as typeof port.closingReportDetail
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  apply('Pending')
  expect(signal?.aborted).toBe(true)
  await act(async () => finish(ok(reportDetail())))
  expect(screen.queryByText('saved-employee')).toBeNull()
  expect(screen.queryByRole('region', { name: 'รายงานปิดที่บันทึกไว้' })).toBeNull()
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', lifecycleStatus: 'Pending', limit: 20 })
})
