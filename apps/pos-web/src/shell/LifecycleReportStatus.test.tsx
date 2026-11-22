import { fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { parseClosingReportDetail, type ClosingLifecycleDelivery, type ClosingReportDetail, type ClosingReportItem, type ClosingReportState } from '../api/closingReportWorkspaceContracts.ts'
import { branch, now, reportItem, reportPage } from '../api/closingReportTestFixtures.ts'
import { frozenLifecycleDetail } from '../api/frozenLifecycleTestFixtures.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function saved(state: ClosingReportState): ClosingReportDetail {
  const value = frozenLifecycleDetail()
  const delivery: ClosingLifecycleDelivery = { contextId: 'lifecycle:' + value.report.reportId, deliveryState: state,
    attemptCount: state === 'Queued' ? 0 : 1, availableAt: state === 'RetryScheduled' ? '2026-11-21T03:01:00Z' : now,
    leaseExpiresAt: state === 'InFlight' ? '2026-11-21T03:01:00Z' : state === 'LeaseExpired' ? now : null,
    deliveredAt: state === 'Delivered' ? now : null, lastFailure: state === 'RetryScheduled' ? 'ParentUnavailable' : null }
  value.report = { ...value.report, deliveryState: 'Delivered', deliveredAt: now, attemptCount: 1, lifecycleDelivery: delivery }
  value.lifecycleDelivery = delivery
  return value
}
const ports = (value: ClosingReportDetail) => ({ closingReports: vi.fn(async () => ok(reportPage([value.report]))),
  closingReportDetail: vi.fn(async () => ok(parseClosingReportDetail(value, branch, value.report.requestId))) })
it.each([['Queued', 'รอส่ง'], ['InFlight', 'อยู่ระหว่างดำเนินการส่ง'], ['LeaseExpired', 'รอกู้คืนการส่ง'],
  ['RetryScheduled', 'รอลองส่งอีกครั้ง'], ['Delivered', 'ส่วนกลางยืนยันแล้ว']] as [ClosingReportState, string][])('shows independent %s row diagnostics without opening detail', async (state, label) => {
  render(<ClosingReportWorkspace port={ports(saved(state))} scopeKey="branch" refreshKey={0} />)
  const row = within(await screen.findByRole('region', { name: 'สถานะข้อมูลเปิดร้านและกะของรายการ' }))
  expect(row.getByText(label)).toBeTruthy()
  expect(row.getByText('จำนวนครั้งที่เริ่มส่งข้อมูลส่วนนี้ของรายการ')).toBeTruthy()
  if (state === 'RetryScheduled') expect(row.getByText(/ไม่ต้องปิดร้านซ้ำ/)).toBeTruthy()
  if (state === 'Delivered') expect(row.getByText('ข้อมูลส่วนนี้ของรายการยืนยันรับเมื่อ')).toBeTruthy()
  else expect(row.queryByText('ข้อมูลส่วนนี้ของรายการยืนยันรับเมื่อ')).toBeNull()
  expect(screen.queryByRole('article')).toBeNull()
  expect(screen.queryByRole('button', { name: /^ส่งใหม่|^Retry|^พิมพ์|^ดาวน์โหลด/ })).toBeNull()
})
it('distinguishes explicit historical absence from unknown older responses in the list', async () => {
  const items: ClosingReportItem[] = [reportItem(), reportItem({ requestId: '00000000-0000-0000-0000-000000000022', lifecycleDelivery: null })]
  render(<ClosingReportWorkspace port={{ closingReports: vi.fn(async () => ok(reportPage(items))) }} scopeKey="branch" refreshKey={0} />)
  const rows = await screen.findAllByRole('region', { name: 'สถานะข้อมูลเปิดร้านและกะของรายการ' })
  expect(within(rows[0]!).getByText('รายการนี้ยังไม่ระบุสถานะส่งข้อมูลเปิดร้านและกะ')).toBeTruthy()
  expect(within(rows[1]!).getByText(/รายงานนี้ไม่มีข้อมูลเปิดร้านและกะที่บันทึกไว้/)).toBeTruthy()
  for (const row of rows) expect(within(row).queryByText('ส่วนกลางยืนยันแล้ว')).toBeNull()
})
it('refreshes row confirmation outside the unchanged frozen document and clears stale success on failure', async () => {
  const pending = saved('InFlight'); const confirmed = saved('Delivered')
  const port = ports(pending)
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  fireEvent.click(await screen.findByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  const frozen = screen.getByRole('article').textContent
  port.closingReports.mockResolvedValue(ok(reportPage([confirmed.report])))
  port.closingReportDetail.mockResolvedValue(ok(parseClosingReportDetail(confirmed, branch, confirmed.report.requestId)))
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={1} />)
  const receipt = within(await screen.findByRole('region', { name: 'สถานะข้อมูลเปิดร้านและกะของรายการ' }))
  expect(receipt.getByText('ข้อมูลส่วนนี้ของรายการยืนยันรับเมื่อ')).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  fireEvent.click(await screen.findByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  expect(screen.getByRole('article').textContent).toBe(frozen)
  port.closingReports.mockResolvedValue({ kind: 'unavailable', reason: 'http', httpStatus: 503 })
  view.rerender(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={2} />)
  await screen.findByRole('alert')
  expect(screen.queryByRole('region', { name: 'สถานะข้อมูลเปิดร้านและกะของรายการ' })).toBeNull()
  expect(screen.queryByRole('article')).toBeNull()
})
