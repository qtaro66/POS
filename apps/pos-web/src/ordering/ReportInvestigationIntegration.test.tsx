import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createPreviewReadClient } from './preview.ts'
import type { OrderingReadClient } from './readController.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import type { ClosingReportPage, ClosingReportQuery } from '../api/closingReportWorkspaceContracts.ts'
import { branch, now, reportPage } from '../api/closingReportTestFixtures.ts'
import { frozenLifecycleDetail } from '../api/frozenLifecycleTestFixtures.ts'
import { deliveryView } from '../api/deliveryOverviewTestFixtures.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function captured() {
  const detail = frozenLifecycleDetail()
  detail.report = { ...detail.report, deliveryState: 'Delivered', deliveredAt: now, attemptCount: 1,
    lifecycleDelivery: { contextId: 'lifecycle:' + detail.report.reportId, deliveryState: 'Queued', attemptCount: 0,
      availableAt: now, leaseExpiresAt: null, deliveredAt: null, lastFailure: null } }
  return { ...detail, lifecycleDelivery: detail.report.lifecycleDelivery }
}
function reader() {
  const detail = captured(), overview = deliveryView(0, 1, 0, 1, 1)
  overview.snapshot.branchId = branch
  const closingReports = vi.fn(async (query: ClosingReportQuery) => ok(reportPage([
    query.status === 'Pending' ? { ...detail.report, deliveryState: 'Queued' as const, deliveredAt: null, attemptCount: 0 }
      : detail.report,
  ])))
  const closingReportDetail = vi.fn(async () => ok(detail))
  const storeLifecycleCommand = vi.fn(async () => ({ kind: 'unavailable' as const, reason: 'network' as const }))
  const client: OrderingReadClient = { ...createPreviewReadClient(), scope: { serviceOrigin: 'https://branch.example', branchId: branch, deviceId: 'device' },
    closingReports, closingReportDetail, deliveryOverview: vi.fn(async () => ok(overview)), storeLifecycleCommand }
  return { client, detail, closingReports, closingReportDetail, storeLifecycleCommand }
}
const nav = () => within(screen.getByRole('navigation', { name: 'เมนูหลัก' }))

it('investigates both independent pending queues without requiring lifecycle commands or disturbing the selected sale', async () => {
  const port = reader(), selectedRead = vi.spyOn(port.client, 'sessionDetail')
  localStorage.setItem('synthetic-investigation-pending', 'unchanged')
  const stored = JSON.stringify(Object.entries(localStorage))
  render(<App client={port.client} />)
  fireEvent.click(await screen.findByRole('button', { name: /A04/ }))
  await screen.findByTestId('net-total')
  fireEvent.click(nav().getByRole('button', { name: 'สถานะระบบ' }))
  fireEvent.click(await screen.findByRole('button', { name: 'ตรวจสอบข้อมูลเปิดร้านและกะที่รอยืนยัน' }))
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', lifecycleStatus: 'Pending', limit: 20 })
  expect(port.closingReportDetail).toHaveBeenCalledWith(port.detail.report.requestId, expect.any(AbortSignal))
  expect(await screen.findByRole('region', { name: 'รายงานปิดที่บันทึกไว้' })).toBeTruthy()
  fireEvent.click(nav().getByRole('button', { name: 'สถานะระบบ' }))
  const before = port.closingReports.mock.calls.length
  fireEvent.click(await screen.findByRole('button', { name: 'ตรวจสอบข้อมูลเปิดร้านและกะที่รอยืนยัน' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls.length).toBeGreaterThan(before)
  expect(screen.queryByRole('region', { name: 'รายงานปิดที่บันทึกไว้' })).toBeNull()
  fireEvent.click(nav().getByRole('button', { name: 'สถานะระบบ' }))
  fireEvent.click(await screen.findByRole('button', { name: 'ตรวจสอบรายงานหลักที่รอยืนยัน' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'Pending', limit: 20 })
  fireEvent.click(nav().getByRole('button', { name: 'รายงานปิด' }))
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(port.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', limit: 20 })
  fireEvent.click(nav().getByRole('button', { name: 'โต๊ะ' }))
  expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
  expect(screen.getByRole('button', { name: /A04/ }).getAttribute('aria-pressed')).toBe('true')
  expect(selectedRead).toHaveBeenCalledTimes(1)
  expect(port.storeLifecycleCommand).not.toHaveBeenCalled()
  expect(JSON.stringify(Object.entries(localStorage))).toBe(stored)
  localStorage.removeItem('synthetic-investigation-pending')
})

it('abandons the old report intent and delayed read on authenticated client replacement even with the same branch scope', async () => {
  const first = reader(), second = reader()
  let finish!: (value: QueryResult<ClosingReportPage>) => void
  let oldSignal: AbortSignal | undefined
  first.closingReports.mockImplementation((_, signal?: AbortSignal) => {
    oldSignal = signal
    return new Promise<QueryResult<ClosingReportPage>>(resolve => { finish = resolve })
  })
  const view = render(<App client={first.client} />)
  fireEvent.click(nav().getByRole('button', { name: 'สถานะระบบ' }))
  fireEvent.click(await screen.findByRole('button', { name: 'ตรวจสอบข้อมูลเปิดร้านและกะที่รอยืนยัน' }))
  await waitFor(() => expect(first.closingReports).toHaveBeenCalled())
  view.rerender(<App client={second.client} />)
  expect(oldSignal?.aborted).toBe(true)
  expect(screen.getByRole('main', { name: 'รายงานปิด' })).toBeTruthy()
  await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  expect(second.closingReports.mock.calls.at(-1)?.[0]).toEqual({ status: 'All', limit: 20 })
  await act(async () => { finish(ok(reportPage([{ ...first.detail.report, shiftId: 'old-authenticated-shift' }]))); await Promise.resolve() })
  expect(screen.queryByText(/old-authenticated-shift/)).toBeNull()
  expect(first.closingReportDetail).not.toHaveBeenCalled()
  expect(first.storeLifecycleCommand).not.toHaveBeenCalled()
  expect(second.storeLifecycleCommand).not.toHaveBeenCalled()
})

it.each(['preview', 'scope', 'detail', 'list'] as const)('keeps report navigation inert without live read capabilities: %s', async missing => {
  const port = reader()
  if (missing === 'scope') delete port.client.scope
  if (missing === 'detail') delete port.client.closingReportDetail
  if (missing === 'list') delete port.client.closingReports
  render(<App client={port.client} preview={missing === 'preview'} />)
  fireEvent.click(nav().getByRole('button', { name: 'สถานะระบบ' }))
  await screen.findByRole('heading', { name: 'สรุปการส่งธุรกรรมและรายงาน' })
  expect(screen.queryByRole('button', { name: 'ตรวจสอบข้อมูลเปิดร้านและกะที่รอยืนยัน' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'ตรวจสอบรายงานหลักที่รอยืนยัน' })).toBeNull()
  fireEvent.click(nav().getByRole('button', { name: 'รายงานปิด' }))
  expect(screen.getByText('หน้านี้ยังไม่เชื่อมต่อข้อมูลและคำสั่ง จึงยังไม่พร้อมใช้งาน')).toBeTruthy()
  expect(port.closingReports).not.toHaveBeenCalled()
  expect(port.closingReportDetail).not.toHaveBeenCalled()
  expect(port.storeLifecycleCommand).not.toHaveBeenCalled()
})
