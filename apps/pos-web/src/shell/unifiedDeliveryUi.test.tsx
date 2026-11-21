import { fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, beforeAll, expect, it, vi } from 'vitest'
import { at, deliveryView } from '../api/deliveryOverviewTestFixtures.ts'
import { DataFreshnessPanel, DataFreshnessSidebar } from './DataFreshnessPanel.tsx'
import { DeliveryOverviewPanel } from './DeliveryOverviewPanel.tsx'
import { HomeCloudStatus } from './HomeCloudStatus.tsx'
import { StoreShiftWorkspace } from './StoreShiftWorkspace.tsx'
import type { FreshnessState } from './dataFreshness.ts'

const state = (reports = 1): FreshnessState => ({ synced: { phase: 'ready', timestamp: null }, sent: { phase: 'ready', timestamp: at, overview: deliveryView(0, reports, 1, 2) } })
afterEach(() => localStorage.clear())
beforeAll(() => { HTMLDialogElement.prototype.showModal = function () { this.open = true } })
it('keeps both sidebar labels and exposes report/replay backlog beside recent confirmation', () => {
  render(<DataFreshnessSidebar state={state()} />)
  const sidebar = within(screen.getByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' }))
  expect(sidebar.getByText('Data Synced')).toBeTruthy(); expect(sidebar.getByText('Data Sent')).toBeTruthy()
  expect(sidebar.getByText('ยังมีรายการรอยืนยัน')).toBeTruthy()
  expect(sidebar.getByText(/ธุรกรรมโต๊ะรอ 0 · รายงานรอ 1 · กู้ลำดับรอ 1/)).toBeTruthy()
  expect(sidebar.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
  expect(sidebar.getAllByRole('time')).toHaveLength(2)
})
it('qualifies legacy-only timestamps and never substitutes them for all-queue completion', () => {
  render(<DataFreshnessSidebar state={{ synced: { phase: 'unsupported' }, sent: { phase: 'ready', timestamp: at } }} />)
  expect(screen.getByText(/เวลาธุรกรรมโต๊ะเดิมเท่านั้น/)).toBeTruthy()
  expect(screen.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
})
it('shows unavailable rather than remembered current state after a failed modern observation', () => {
  const view = render(<DataFreshnessSidebar state={{ synced: { phase: 'unsupported' }, sent: { phase: 'ready', timestamp: at, overview: deliveryView(0, 0, 0, 1) } }} />)
  expect(screen.getByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeTruthy()
  view.rerender(<DataFreshnessSidebar state={{ synced: { phase: 'unsupported' }, sent: { phase: 'unavailable' } }} />)
  expect(screen.getByText('อ่านข้อมูลไม่ได้')).toBeTruthy()
  expect(screen.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
})
it('shows channel breakdown, observation and configuration warnings without network claims or mutation buttons', () => {
  const overview = deliveryView(1, 1, 1, 1)
  overview.tableDeliveryEnabled = false; overview.reportDeliveryEnabled = false
  overview.snapshot.tableEvents.expiredLeaseCount = 1; overview.snapshot.closingReports.unknownCount = 1
  render(<DeliveryOverviewPanel reading={{ phase: 'ready', timestamp: at, overview }} />)
  const panel = within(screen.getByRole('region', { name: 'สรุปการส่งธุรกรรมและรายงาน' }))
  expect(panel.getByRole('region', { name: 'ธุรกรรมโต๊ะ' })).toBeTruthy()
  expect(panel.getByRole('region', { name: 'รายงานปิดกะและปิดร้าน' })).toBeTruthy()
  expect(panel.getByRole('region', { name: 'งานกู้ลำดับข้อมูล' })).toBeTruthy()
  expect(panel.getByText(/มีรายงานรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่งรายงาน/)).toBeTruthy()
  expect(panel.getByText(/มีธุรกรรมหรืองานกู้ลำดับรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่ง/)).toBeTruthy()
  expect(panel.getByText(/ไม่ใช่การยืนยันว่าส่วนกลางพร้อมใช้งาน/)).toBeTruthy()
  expect(panel.queryAllByRole('button')).toHaveLength(0)
})
it('system status uses the same aggregate observation and exposes independent refresh', () => {
  const refresh = vi.fn()
  render(<DataFreshnessPanel state={state()} onRefresh={refresh} />)
  expect(screen.getByRole('region', { name: 'สรุปการส่งธุรกรรมและรายงาน' })).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'อ่านสถานะใหม่' })); expect(refresh).toHaveBeenCalledOnce()
})
it('home summary does not hide pending reports behind a recent table acknowledgement', async () => {
  render(<HomeCloudStatus port={{}} freshness={state()} openDetails={() => {}} />)
  expect(await screen.findByText('ยังมีรายการรอยืนยัน')).toBeTruthy(); expect(screen.getByText(/รายงานรอ 1/)).toBeTruthy()
})
it('accepted-close notification failure cannot change committed closing state or retain browser recovery', async () => {
  const store = '00000000-0000-4000-8000-000000000001', shift = '00000000-0000-4000-8000-000000000002'
  const current = { storeSessionId: store, activeShiftId: shift, isClosed: false, businessDate: '2026-11-21', openingCashAmount: 500, closing: null }
  const accepted = vi.fn(() => { throw new Error('observer unavailable') })
  const command = vi.fn(async () => ({ status: 200, body: { ...current, activeShiftId: null } }))
  render(<StoreShiftWorkspace storageKey="accepted-observer" onAccepted={accepted} port={{ storeLifecycleCurrent: async () => ({ kind: 'ok', value: current }), storeLifecycleCommand: command }} />)
  await screen.findByText('ร้านเปิด · มีกะเปิดอยู่'); fireEvent.click(screen.getByRole('button', { name: 'ปิดกะ' }))
  const dialog = within(screen.getByRole('dialog'))
  fireEvent.change(dialog.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(dialog.getByRole('button', { name: 'ยืนยันปิดกะ' }).closest('form')!)
  await screen.findByText('ร้านเปิด · ระหว่างกะ')
  expect(command).toHaveBeenCalledOnce(); expect(accepted).toHaveBeenCalledOnce()
  expect(localStorage.getItem('accepted-observer')).toBeNull(); expect(screen.queryByText(/ยังไม่ทราบผลคำขอ/)).toBeNull()
})

it('uses only the unified aggregate for modern clients even when legacy report readers exist', () => {
  const overview = deliveryView(0, 1, 0, 1)
  const legacyQueue = vi.fn()
  const legacySettings = vi.fn()
  const port = { deliveryOverview: async () => ({ kind: 'ok' as const, value: overview }), closingReportQueueStatus: legacyQueue, closingReportDelivery: legacySettings }
  const view = render(<DataFreshnessPanel state={state()} onRefresh={() => {}} closingReportPort={port} />)
  expect(screen.getByRole('region', { name: 'สรุปการส่งธุรกรรมและรายงาน' })).toBeTruthy()
  expect(screen.queryByRole('region', { name: 'รายงานปิดกะและปิดร้านรอส่ง' })).toBeNull()
  expect(legacyQueue).not.toHaveBeenCalled(); expect(legacySettings).not.toHaveBeenCalled()
  view.rerender(<DataFreshnessPanel state={{ synced: { phase: 'unsupported' }, sent: { phase: 'unavailable' } }} onRefresh={() => {}} closingReportPort={port} />)
  expect(legacyQueue).not.toHaveBeenCalled(); expect(legacySettings).not.toHaveBeenCalled()
  expect(screen.queryByText('รายการในคิวที่อ่านได้รับการยืนยันแล้ว')).toBeNull()
})
