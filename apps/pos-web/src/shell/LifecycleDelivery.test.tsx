import { fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { DataFreshnessPanel, DataFreshnessSidebar } from './DataFreshnessPanel.tsx'
import { frozenLifecycleDetail } from '../api/frozenLifecycleTestFixtures.ts'
import { branch, now, reportPage } from '../api/closingReportTestFixtures.ts'
import { deliveryView } from '../api/deliveryOverviewTestFixtures.ts'
import { parseClosingReportDetail, type ClosingLifecycleDelivery, type ClosingReportDetail } from '../api/closingReportWorkspaceContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function saved(delivered = false) {
  const value = frozenLifecycleDetail()
  value.report.deliveryState = 'Delivered'; value.report.deliveredAt = now; value.report.attemptCount = 1
  const lifecycleDelivery: ClosingLifecycleDelivery = { contextId: 'lifecycle:' + value.report.reportId,
    deliveryState: delivered ? 'Delivered' : 'RetryScheduled', attemptCount: 2, availableAt: now,
    leaseExpiresAt: null, deliveredAt: delivered ? now : null, lastFailure: delivered ? null : 'ParentUnavailable' }
  return { ...value, lifecycleDelivery }
}
const port = (value: ClosingReportDetail) => ({ closingReports: vi.fn(async () => ok(reportPage([value.report]))),
  closingReportDetail: vi.fn(async () => ok(parseClosingReportDetail(value, branch, value.report.requestId))) })

it('separates accepted parent from delayed context and describes repair without another close command', async () => {
  render(<ClosingReportWorkspace port={port(saved())} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  const delivery = within(await screen.findByRole('region', { name: 'สถานะส่งข้อมูลเปิดร้านและกะ' }))
  expect(await delivery.findByText('ข้อมูลเปิดร้านและกะยังรอยืนยันจากส่วนกลาง')).toBeTruthy()
  expect(delivery.getByText(/ไม่ต้องปิดร้านซ้ำ/)).toBeTruthy()
  expect(delivery.getByText(/การยืนยันรับรายงานหลักไม่ได้ยืนยันรับข้อมูลเปิดร้านและกะส่วนนี้/)).toBeTruthy()
  expect(delivery.queryByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')).toBeNull()
  expect(screen.queryByRole('button', { name: /^ส่งใหม่|^Retry|^พิมพ์|^ดาวน์โหลด/ })).toBeNull()
})
it('keeps the document body frozen when independent context receipt changes after refresh', async () => {
  const view = render(<ClosingReportWorkspace port={port(saved())} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  fireEvent.click(await screen.findByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  const before = screen.getByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' }).textContent
  expect(before).not.toContain('รอระบบกู้คืน')
  expect(before).not.toContain('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')
  view.rerender(<ClosingReportWorkspace port={port(saved(true))} scopeKey="branch" refreshKey={1} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')
  fireEvent.click(screen.getByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  expect(screen.getByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' }).textContent).toBe(before)
})
it('keeps old context receipt status unknown and does not infer acceptance from the parent', async () => {
  const value = saved(); delete (value as ClosingReportDetail).lifecycleDelivery
  render(<ClosingReportWorkspace port={port(value)} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  expect(await screen.findByText('ยังไม่ทราบสถานะส่งข้อมูลเปิดร้านและกะ')).toBeTruthy()
  expect(screen.queryByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')).toBeNull()
})
it('shows context backlog on Sidebar and System Status even after base receipt and warns when disabled', () => {
  const overview = deliveryView(0, 0, 0, 1, 1); overview.lifecycleDeliveryEnabled = false
  const state = { synced: { phase: 'unsupported' as const }, sent: { phase: 'ready' as const, timestamp: overview.snapshot.lastConfirmedAt, overview } }
  render(<><DataFreshnessSidebar state={state} /><DataFreshnessPanel state={state} onRefresh={() => {}} /></>)
  const sidebar = within(screen.getByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' }))
  expect(sidebar.getByText(/บริบทกะรอ 1/)).toBeTruthy()
  expect(sidebar.getByText('ยังมีรายการรอยืนยัน')).toBeTruthy()
  expect(sidebar.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
  const panel = within(screen.getByRole('region', { name: 'สรุปการส่งธุรกรรมและรายงาน' }))
  expect(panel.getByRole('region', { name: 'ข้อมูลเปิดร้านและกะ' })).toBeTruthy()
  expect(panel.getByText(/มีข้อมูลเปิดร้านและกะรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่งข้อมูลส่วนนี้/)).toBeTruthy()
})
