import { useSyncExternalStore } from 'react'
import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { DataFreshnessPanel, DataFreshnessSidebar } from './DataFreshnessPanel.tsx'
import { createDataFreshness } from './dataFreshness.ts'
import { frozenLifecycleDetail } from '../api/frozenLifecycleTestFixtures.ts'
import { branch, hex, now, reportPage, request } from '../api/closingReportTestFixtures.ts'
import { deliveryView } from '../api/deliveryOverviewTestFixtures.ts'
import { parseClosingReportDetail, type ClosingLifecycleDelivery, type ClosingReportDetail } from '../api/closingReportWorkspaceContracts.ts'
import { parseDeliveryOverview, type DeliveryOverview } from '../api/deliveryOverviewContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(finish => { resolve = finish }); return { promise, resolve } }
function detail(state: ClosingLifecycleDelivery['deliveryState'], requestId = request) {
  const value = frozenLifecycleDetail()
  value.report.requestId = requestId; value.report.reportId = 'closing:' + hex(branch) + ':' + hex(requestId)
  value.snapshot.requestId = requestId; value.snapshot.reportId = value.report.reportId; value.lifecycle.requestId = requestId
  value.report.deliveryState = 'Delivered'; value.report.attemptCount = 1; value.report.deliveredAt = now
  const leased = state === 'InFlight' || state === 'LeaseExpired'
  const lifecycleDelivery: ClosingLifecycleDelivery = { contextId: 'lifecycle:' + value.report.reportId, deliveryState: state,
    attemptCount: state === 'Queued' ? 0 : 2, availableAt: now, leaseExpiresAt: leased ? now : null,
    deliveredAt: state === 'Delivered' ? now : null, lastFailure: state === 'RetryScheduled' ? 'AcknowledgmentUnavailable' : null }
  return parseClosingReportDetail({ ...value, lifecycleDelivery }, branch, requestId)
}
const port = (value: ClosingReportDetail) => ({ closingReports: vi.fn(async () => ok(reportPage([value.report]))),
  closingReportDetail: vi.fn(async () => ok(value)) })

it('does not let a late receipt for another selected report clear the active report companion backlog', async () => {
  const old = detail('Delivered'), active = detail('RetryScheduled', '00000000-0000-0000-0000-000000000004')
  const late = deferred<QueryResult<ClosingReportDetail>>()
  let signal: AbortSignal | undefined
  const read = vi.fn((id: string, next?: AbortSignal) => {
    if (id === old.report.requestId) { signal = next; return late.promise }
    return Promise.resolve(ok(active))
  })
  render(<ClosingReportWorkspace port={{ closingReports: async () => ok(reportPage([old.report, active.report])), closingReportDetail: read }} scopeKey="branch" refreshKey={0} />)
  const buttons = await screen.findAllByRole('button', { name: 'ดูรายงานที่บันทึกไว้' })
  fireEvent.click(buttons[0])
  await screen.findByText('กำลังอ่านรายงานที่บันทึกไว้')
  fireEvent.click(buttons[1])
  const delivery = within(await screen.findByRole('region', { name: 'สถานะส่งข้อมูลเปิดร้านและกะ' }))
  await delivery.findByText('ข้อมูลเปิดร้านและกะยังรอยืนยันจากส่วนกลาง')
  expect(signal?.aborted).toBe(true)
  await act(async () => late.resolve(ok(old)))
  expect(delivery.getByText('รอลองส่งอีกครั้ง')).toBeTruthy()
  expect(delivery.getByText('ยังไม่ได้รับคำยืนยันที่ตรงกับรายงาน')).toBeTruthy()
  expect(delivery.queryByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว')).toBeNull()
  expect(buttons[1].getAttribute('aria-pressed')).toBe('true')
  expect(read.mock.calls.map(call => call[0])).toEqual([old.report.requestId, active.report.requestId])
})

it('keeps the frozen document and parent receipt unchanged through expired-lease and delayed companion recovery', async () => {
  const view = render(<ClosingReportWorkspace port={port(detail('InFlight'))} scopeKey="branch" refreshKey={0} />)
  let document: string | null = null
  const labels = { InFlight: 'อยู่ระหว่างดำเนินการส่ง', LeaseExpired: 'รอกู้คืนการส่ง', RetryScheduled: 'รอลองส่งอีกครั้ง', Delivered: 'ส่วนกลางยืนยันแล้ว' }
  for (const [revision, state] of (['InFlight', 'LeaseExpired', 'RetryScheduled', 'Delivered'] as const).entries()) {
    if (revision) view.rerender(<ClosingReportWorkspace port={port(detail(state))} scopeKey="branch" refreshKey={revision} />)
    fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
    const delivery = within(await screen.findByRole('region', { name: 'สถานะส่งข้อมูลเปิดร้านและกะ' }))
    expect(delivery.getByText(labels[state])).toBeTruthy()
    expect(delivery.queryByText('ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว') !== null).toBe(state === 'Delivered')
    expect(within(screen.getByRole('region', { name: 'สถานะส่งล่าสุดของรายงาน' })).getByText('ส่วนกลางยืนยันแล้ว')).toBeTruthy()
    fireEvent.click(screen.getByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
    const article = screen.getByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' })
    if (document === null) document = article.textContent
    expect(article.textContent).toBe(document)
    expect(article.textContent).not.toContain(labels[state])
    expect(screen.queryByRole('button', { name: /^ส่งใหม่|^Retry|^พิมพ์|^ดาวน์โหลด/ })).toBeNull()
  }
})

it('removes stale success during context recovery and failed overview reads on both status surfaces', async () => {
  const current = deliveryView(0, 0, 0, 1), pending = deliveryView(0, 0, 0, 1, 1)
  pending.snapshot.lifecycleReports.expiredLeaseCount = 1
  const lateSuccess = deferred<QueryResult<DeliveryOverview>>()
  let superseded: AbortSignal | undefined
  const legacy = vi.fn(async () => ok(now))
  const read = vi.fn().mockResolvedValueOnce(ok(current)).mockImplementationOnce((signal?: AbortSignal) => { superseded = signal; return lateSuccess.promise })
    .mockResolvedValueOnce(ok(parseDeliveryOverview(pending, pending.snapshot.branchId)))
    .mockResolvedValueOnce({ kind: 'unavailable', reason: 'http', httpStatus: 503 }).mockResolvedValueOnce(ok(current))
  const controller = createDataFreshness({ deliveryOverview: read, dataSentStatus: legacy })
  function Status() {
    const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot)
    return <><DataFreshnessSidebar state={state} /><DataFreshnessPanel state={state} onRefresh={() => { void controller.refresh() }} /></>
  }
  const view = render(<Status />)
  await act(async () => { await controller.refresh() })
  const sidebar = within(screen.getByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' }))
  expect(sidebar.getByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeTruthy()
  let obsolete!: Promise<void>
  act(() => { obsolete = controller.refresh() })
  expect(sidebar.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
  expect(screen.queryByRole('region', { name: 'ข้อมูลเปิดร้านและกะ' })).toBeNull()
  await act(async () => { await controller.refresh() })
  expect(superseded?.aborted).toBe(true)
  await act(async () => { lateSuccess.resolve(ok(current)); await obsolete })
  expect(sidebar.getByText('ยังมีรายการรอยืนยัน')).toBeTruthy()
  expect(sidebar.getByText(/บริบทกะรอ 1/)).toBeTruthy()
  const channel = within(screen.getByRole('region', { name: 'ข้อมูลเปิดร้านและกะ' }))
  expect(channel.getByText('1 / 0 / 1')).toBeTruthy()
  expect(channel.getByText('การส่งเดิมหมดช่วงเวลาที่จองไว้').nextElementSibling?.textContent).toBe('1')
  await act(async () => { await controller.refresh() })
  expect(sidebar.getByText('อ่านข้อมูลไม่ได้')).toBeTruthy()
  expect(sidebar.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
  expect(screen.queryByRole('region', { name: 'ข้อมูลเปิดร้านและกะ' })).toBeNull()
  expect(screen.getByText('ยังยืนยันภาพรวมทุกคิวไม่ได้ ไม่ใช้เวลาธุรกรรมเดิมแทนจำนวนรายงานที่ค้างอยู่')).toBeTruthy()
  await act(async () => { await controller.refresh() })
  expect(sidebar.getByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeTruthy()
  expect(legacy).not.toHaveBeenCalled()
  view.unmount(); controller.cancel()
})
