import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { ClosingReportWorkspace } from './ClosingReportWorkspace.tsx'
import { frozenLifecycleDetail } from '../api/frozenLifecycleTestFixtures.ts'
import { branch, reportDetail, reportPage } from '../api/closingReportTestFixtures.ts'
import { parseClosingReportDetail, type ClosingReportDetail } from '../api/closingReportWorkspaceContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const ports = (value: ClosingReportDetail) => ({ closingReports: vi.fn(async () => ok(reportPage([value.report]))),
  closingReportDetail: vi.fn(async () => ok(parseClosingReportDetail(value, branch, value.report.requestId))) })
async function open(value = frozenLifecycleDetail()) {
  const port = ports(value)
  const view = render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByRole('region', { name: 'บริบทร้านและกะ ณ เวลาปิด' })
  return { port, view }
}
it('shows original opening attribution and every captured shift without summing shift floats', async () => {
  await open()
  const life = within(screen.getByRole('region', { name: 'บริบทร้านและกะ ณ เวลาปิด' }))
  expect(life.getByText('ผู้เปิดร้าน').nextElementSibling?.textContent).toBe('store-opener')
  expect(life.getByText('อุปกรณ์เปิดร้าน').nextElementSibling?.textContent).toBe('opening-device')
  expect(life.getByText('3 กะ')).toBeTruthy()
  const first = within(life.getByRole('region', { name: 'กะที่บันทึก 1' }))
  const second = within(life.getByRole('region', { name: 'กะที่บันทึก 2' }))
  const last = within(life.getByRole('region', { name: 'กะที่บันทึก 3' }))
  expect(first.getByText('ไม่ได้กระทบยอด (NotReconciled)')).toBeTruthy()
  expect(second.getByText('200.00 บาท')).toBeTruthy()
  expect(second.getByText('0.00 บาท')).toBeTruthy()
  expect(last.getByText('ไม่ได้ระบุ')).toBeTruthy()
  expect(life.getByRole('region', { name: 'กะที่ไม่ได้กระทบยอด' }).querySelectorAll('li')).toHaveLength(1)
  expect(life.getByText(/ไม่บวกเพิ่มเข้ายอดเงินสดตั้งต้น/)).toBeTruthy()
  expect(life.queryByText('700.00 บาท')).toBeNull()
  expect(life.getByText(/ยังไม่รวมในการส่งไปส่วนกลาง/)).toBeTruthy()
})
it('opens a read-only document view with frozen financials and no hardware or download controls', async () => {
  const { port } = await open()
  fireEvent.click(screen.getByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  const document = within(screen.getByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' }))
  expect(document.getByRole('heading', { name: /รายงานปิดร้าน/ })).toBeTruthy()
  expect(document.getByText('VAT รวมในราคา')).toBeTruthy()
  expect(document.getByText('2 โต๊ะ')).toBeTruthy()
  expect(document.getAllByText('-10.00 บาท')).toHaveLength(2)
  expect(document.queryByText('สถานะการส่ง')).toBeNull()
  expect(screen.getByText(/ยังไม่สั่งพิมพ์หรือส่งออกไฟล์/)).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^พิมพ์|^ส่งออก|^ดาวน์โหลด/ })).toBeNull()
  expect(port.closingReportDetail).toHaveBeenCalledTimes(1)
  fireEvent.click(screen.getByRole('button', { name: 'กลับมุมมองรายละเอียดรายงาน' }))
  expect(screen.queryByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' })).toBeNull()
})
it('never reconstructs legacy context or pretends the missing shift count is zero', async () => {
  const saved = reportDetail(), port = ports(saved)
  render(<ClosingReportWorkspace port={port} scopeKey="branch" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByText(/รายงานเดิมยังไม่มีบริบทเปิดร้าน/)
  expect(screen.queryByRole('region', { name: 'กะที่ไม่ได้กระทบยอด' })).toBeNull()
  expect(screen.queryByText('0 กะ')).toBeNull()
  expect(port.closingReportDetail).toHaveBeenCalledTimes(1)
})
it('escapes stored attribution and preserves all shifts in a large frozen context', async () => {
  const value = frozenLifecycleDetail(), first = value.lifecycle.shifts[0]
  value.lifecycle.opened.employeeId = '<img src=x onerror=alert(1)>'
  first.opened = { ...value.lifecycle.opened }
  const earlier = Array.from({ length: 70 }, (_, index) => ({ ...structuredClone(first),
    shiftId: `00000000-0000-0000-0000-${String(index + 1000).padStart(12, '0')}` }))
  for (const shift of earlier) shift.closing.shiftId = shift.shiftId
  value.lifecycle.shifts = [...earlier, value.lifecycle.shifts.at(-1)!]
  await open(value)
  const life = screen.getByRole('region', { name: 'บริบทร้านและกะ ณ เวลาปิด' })
  expect(within(life).getByText('71 กะ')).toBeTruthy()
  expect(within(life).getByRole('region', { name: 'กะที่บันทึก 71' })).toBeTruthy()
  expect(life.querySelector('img')).toBeNull()
  expect(within(life).getAllByText('<img src=x onerror=alert(1)>').length).toBeGreaterThan(0)
})
it('clears the document on refresh and does not revive a late prior-scope result', async () => {
  const saved = frozenLifecycleDetail(), first = ports(saved)
  let finish!: (value: QueryResult<ClosingReportDetail>) => void
  const pending = vi.fn(() => new Promise<QueryResult<ClosingReportDetail>>(resolve => { finish = resolve }))
  const view = render(<ClosingReportWorkspace port={{ ...first, closingReportDetail: pending }} scopeKey="first" refreshKey={0} />)
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByText('กำลังอ่านรายงานที่บันทึกไว้')
  view.rerender(<ClosingReportWorkspace port={first} scopeKey="next" refreshKey={1} />)
  await act(async () => finish(ok(saved)))
  expect(screen.queryByRole('region', { name: 'บริบทร้านและกะ ณ เวลาปิด' })).toBeNull()
  fireEvent.click(await screen.findByRole('button', { name: 'ดูรายงานที่บันทึกไว้' }))
  await screen.findByRole('region', { name: 'บริบทร้านและกะ ณ เวลาปิด' })
  fireEvent.click(screen.getByRole('button', { name: 'เปิดตัวอย่างเอกสารรายงาน' }))
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบรายการรายงานใหม่' }))
  expect(screen.queryByRole('article', { name: 'ตัวอย่างเอกสารรายงานปิด' })).toBeNull()
})
