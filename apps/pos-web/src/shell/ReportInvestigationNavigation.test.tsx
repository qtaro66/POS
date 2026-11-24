import { fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { PcPosShell } from './PcPosShell.tsx'
import { DataFreshnessPanel } from './DataFreshnessPanel.tsx'
import { DeliveryOverviewPanel } from './DeliveryOverviewPanel.tsx'
import { deliveryView } from '../api/deliveryOverviewTestFixtures.ts'
import type { FreshnessReading, FreshnessState } from './dataFreshness.ts'
import type { ReportInvestigationRequest } from './reportInvestigation.ts'

const baseAction = 'ตรวจสอบรายงานหลักที่รอยืนยัน'
const contextAction = 'ตรวจสอบข้อมูลเปิดร้านและกะที่รอยืนยัน'
const reading = (base = 0, context = 0): FreshnessReading => ({ phase: 'ready', timestamp: null, overview: deliveryView(0, base, 0, 0, context) })
const state = (sent: FreshnessReading): FreshnessState => ({ synced: { phase: 'unsupported' }, sent })

it.each([[0, 0], [1, 0], [0, 1], [1, 1]])('offers only known pending report channels: base %i, context %i', (base, context) => {
  const open = vi.fn()
  render(<DeliveryOverviewPanel reading={reading(base, context)} onOpenReports={open} />)
  expect(screen.queryByRole('button', { name: baseAction }) !== null).toBe(base > 0)
  expect(screen.queryByRole('button', { name: contextAction }) !== null).toBe(context > 0)
  if (base) { fireEvent.click(screen.getByRole('button', { name: baseAction })); expect(open).toHaveBeenLastCalledWith('base-pending') }
  if (context) { fireEvent.click(screen.getByRole('button', { name: contextAction })); expect(open).toHaveBeenLastCalledWith('lifecycle-pending') }
})

it.each(['loading', 'unavailable', 'unsupported'] as const)('does not offer investigation for a %s overview', phase => {
  render(<DeliveryOverviewPanel reading={{ phase }} onOpenReports={vi.fn()} />)
  expect(screen.queryByRole('button')).toBeNull()
})

it('does not infer pending report channels from old timestamps, table/replay queues or an absent callback', () => {
  const open = vi.fn()
  const view = render(<DeliveryOverviewPanel reading={{ phase: 'ready', timestamp: '2026-11-21T03:00:00Z' }} onOpenReports={open} />)
  expect(screen.queryByRole('button')).toBeNull()
  view.rerender(<DeliveryOverviewPanel reading={{ phase: 'ready', timestamp: null, overview: deliveryView(2, 0, 2) }} onOpenReports={open} />)
  expect(screen.queryByRole('button')).toBeNull()
  view.rerender(<DeliveryOverviewPanel reading={reading(1, 1)} />)
  expect(screen.queryByRole('button')).toBeNull()
  expect(open).not.toHaveBeenCalled()
})

it('retains read-only investigation when senders are disabled and clears actions during refresh or failure', () => {
  const open = vi.fn(), overview = deliveryView(0, 1, 0, 0, 1)
  overview.reportDeliveryEnabled = false; overview.lifecycleDeliveryEnabled = false
  const view = render(<DataFreshnessPanel state={state({ phase: 'ready', timestamp: null, overview })} onRefresh={() => {}} onOpenReports={open} />)
  fireEvent.click(screen.getByRole('button', { name: contextAction }))
  expect(open).toHaveBeenCalledWith('lifecycle-pending')
  expect(screen.getByText(/ยังไม่ได้ตั้งค่าเปิดส่งข้อมูลส่วนนี้/)).toBeTruthy()
  view.rerender(<DataFreshnessPanel state={state({ phase: 'loading' })} onRefresh={() => {}} onOpenReports={open} />)
  expect(screen.queryByRole('button', { name: contextAction })).toBeNull()
  expect(screen.queryByRole('button', { name: baseAction })).toBeNull()
  view.rerender(<DataFreshnessPanel state={state({ phase: 'unavailable' })} onRefresh={() => {}} onOpenReports={open} />)
  expect(screen.queryByRole('button', { name: contextAction })).toBeNull()
  expect(open).toHaveBeenCalledTimes(1)
})

it('opens targeted reports with fresh navigation revisions and restores All from the sidebar', () => {
  const requests: ReportInvestigationRequest[] = []
  render(<PcPosShell branchLabel="Synthetic branch" preview={false} onLock={() => {}}
    systemPage={open => <DataFreshnessPanel state={state(reading(1, 1))} onRefresh={() => {}} onOpenReports={open} />}
    reportsPage={request => { requests.push(request); return <h2 id="closing-report-workspace-title" tabIndex={-1}>{request.target}:{request.revision}</h2> }}>
    <p>Existing table workspace</p>
  </PcPosShell>)
  const navigation = within(screen.getByRole('navigation', { name: 'เมนูหลัก' }))
  fireEvent.click(navigation.getByRole('button', { name: 'สถานะระบบ' }))
  expect(document.activeElement).toBe(screen.getByRole('main', { name: 'สถานะระบบ' }))
  fireEvent.click(screen.getByRole('button', { name: contextAction }))
  expect(requests.at(-1)).toEqual({ target: 'lifecycle-pending', revision: 1 })
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'lifecycle-pending:1' }))
  fireEvent.click(navigation.getByRole('button', { name: 'สถานะระบบ' }))
  fireEvent.click(screen.getByRole('button', { name: contextAction }))
  expect(requests.at(-1)).toEqual({ target: 'lifecycle-pending', revision: 2 })
  fireEvent.click(navigation.getByRole('button', { name: 'รายงานปิด' }))
  expect(requests.at(-1)).toEqual({ target: 'all', revision: 3 })
  fireEvent.click(navigation.getByRole('button', { name: 'รายงานปิด' }))
  expect(requests.at(-1)).toEqual({ target: 'all', revision: 4 })
  expect(document.activeElement).toBe(screen.getByRole('heading', { name: 'all:4' }))
  fireEvent.click(navigation.getByRole('button', { name: 'สถานะระบบ' }))
  fireEvent.click(screen.getByRole('button', { name: baseAction }))
  expect(requests.at(-1)).toEqual({ target: 'base-pending', revision: 5 })
  fireEvent.click(navigation.getByRole('button', { name: 'โต๊ะ' }))
  expect(screen.getByText('Existing table workspace')).toBeTruthy()
})

it('keeps existing static system/store props and uses a safe unavailable reports page without a report renderer', () => {
  render(<PcPosShell branchLabel="Synthetic" preview={false} onLock={() => {}}
    systemPage={<h2>Existing system page</h2>} storePage={<h2>Existing store page</h2>}><p>Tables</p></PcPosShell>)
  const navigation = within(screen.getByRole('navigation', { name: 'เมนูหลัก' }))
  fireEvent.click(navigation.getByRole('button', { name: 'สถานะระบบ' }))
  expect(screen.getByRole('heading', { name: 'Existing system page' })).toBeTruthy()
  fireEvent.click(navigation.getByRole('button', { name: 'ร้านและกะ' }))
  expect(screen.getByRole('heading', { name: 'Existing store page' })).toBeTruthy()
  fireEvent.click(navigation.getByRole('button', { name: 'รายงานปิด' }))
  expect(screen.getByRole('status').textContent).toContain('ยังไม่พร้อมใช้งาน')
  expect(document.activeElement).toBe(screen.getByRole('main', { name: 'รายงานปิด' }))
})
