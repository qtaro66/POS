import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import type { PrinterInventoryPage } from '../api/printerInventoryContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { PrinterInventoryPanel } from './PrinterInventoryPanel.tsx'
import { createPrinterInventory } from './printerInventory.ts'

type Result = QueryResult<PrinterInventoryPage>
const empty = (): Result => ({ kind: 'ok', value: { schemaVersion: 1, observedAt: '2026-11-02T11:00:00Z', totalCount: 0, activeCount: 0, items: [], nextAfterPrinterId: null } })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

describe('PC POS printer configuration inventory', () => {
  it('reads only on System, shows configuration without inventing live health, pages and refreshes', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), read = vi.fn(fixture.client.printerInventory)
    render(<App client={{ ...fixture.client, printerInventory: read }} />)
    await screen.findByRole('button', { name: /A04/ }); expect(read).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const first = within(await screen.findByRole('article', { name: 'เครื่องพิมพ์ DEMO-PRINTER-1' }))
    expect(first.getByText('printer.branch.test / 9100')).toBeTruthy(); expect(first.getByText('Unknown')).toBeTruthy()
    expect(first.getByText(/Unsupported \(ไม่ใช่ผลตรวจอุปกรณ์\)/)).toBeTruthy()
    expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'ทะเบียนเครื่องพิมพ์หน้าถัดไป' }))
    const second = within(await screen.findByRole('article', { name: 'เครื่องพิมพ์ DEMO-PRINTER-2' }))
    expect(second.getByText('Unknown')).toBeTruthy(); expect(second.getByText('Cashier / Inactive')).toBeTruthy()
    expect(second.getByText(/ไม่แสดงปลายทางที่รูปแบบไม่รองรับ/)).toBeTruthy()
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('region', { name: 'รายการทะเบียนเครื่องพิมพ์' })))
    expect(read.mock.calls[1][0]).toBe('DEMO-PRINTER-1')
    await user.click(screen.getByRole('button', { name: 'อ่านทะเบียนเครื่องพิมพ์ใหม่' }))
    await screen.findByRole('article', { name: 'เครื่องพิมพ์ DEMO-PRINTER-1' }); expect(read.mock.calls[2][0]).toBeUndefined()
    expect(screen.queryByRole('button', { name: /^(ทดสอบ|พิมพ์|เปิดลิ้นชัก|แก้ Profile)/ })).toBeNull(); expect(fixture.state.postedBodies).toHaveLength(0)
  })
  it('distinguishes unsupported, unavailable and observed-empty configuration without raw errors', async () => {
    const user = userEvent.setup(), read = vi.fn(async (): Promise<Result> => { throw new Error('PRIVATE'); })
    const view = render(<PrinterInventoryPanel port={{}} />); expect(screen.getByText('ยังไม่เชื่อมต่อทะเบียนเครื่องพิมพ์')).toBeTruthy()
    view.rerender(<PrinterInventoryPanel port={{ printerInventory: read }} />); await screen.findByRole('alert')
    expect(screen.queryByText('PRIVATE')).toBeNull(); expect(screen.queryByText(/ตั้งค่าไว้ทั้งหมด/)).toBeNull()
    read.mockResolvedValue(empty()); await user.click(screen.getByRole('button', { name: 'อ่านทะเบียนเครื่องพิมพ์ใหม่' }))
    await screen.findByText(/ไม่พบเครื่องพิมพ์ในหน้าที่อ่าน/); expect(screen.getByText(/ไม่ยืนยันว่าทุกเครื่องออนไลน์/)).toBeTruthy()
  })
  it('clears old records while loading and ignores stale responses after refresh', async () => {
    const pending = deferred<Result>(), fixture = createOrderingPreview(), first = await fixture.client.printerInventory()
    const read = vi.fn().mockResolvedValueOnce(first).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(empty())
    const controller = createPrinterInventory({ printerInventory: read })
    await controller.refresh(); const old = controller.refresh(); expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect((read.mock.calls[1][1] as AbortSignal).aborted).toBe(true)
    pending.resolve(first); await old; expect(controller.getSnapshot().page?.totalCount).toBe(0)
  })
  it('does not turn a failed next page into empty configuration and refreshes from page one', async () => {
    const fixture = createOrderingPreview(), first = await fixture.client.printerInventory()
    const read = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce({ kind: 'unavailable', reason: 'http', httpStatus: 503 }).mockResolvedValueOnce(empty())
    const controller = createPrinterInventory({ printerInventory: read })
    await controller.refresh(); await controller.next(); expect(controller.getSnapshot().phase).toBe('unavailable'); expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect(read.mock.calls[2][0]).toBeUndefined(); expect(controller.getSnapshot().pageNumber).toBe(1)
  })
  it('isolates client replacement and removes inventory from the lock screen', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>(); let signal: AbortSignal | undefined
    const view = render(<App client={{ ...fixture.client, printerInventory: async (_, value) => { signal = value; return pending.promise } }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' })); const previous = signal
    view.rerender(<App client={{ ...fixture.client, printerInventory: async () => empty() }} />)
    await screen.findByText(/ไม่พบเครื่องพิมพ์ในหน้าที่อ่าน/); expect(previous?.aborted).toBe(true)
    await act(async () => { pending.resolve({ kind: 'unavailable', reason: 'network' }) }); expect(screen.queryByRole('alert')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' })); expect(screen.queryByRole('heading', { name: 'ทะเบียนเครื่องพิมพ์' })).toBeNull()
  })
  it('preserves basket data and cancels inventory reads when leaving System', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>(); let signal: AbortSignal | undefined
    render(<App client={{ ...fixture.client, printerInventory: async (_, value) => { signal = value; return pending.promise } }} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ })); await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })); await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull()); const before = await fixture.baskets.load(fixture.scope)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' })); await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    await screen.findByRole('button', { name: 'เพิ่มจำนวน น้ำเปล่า' }); expect(signal?.aborted).toBe(true)
    await act(async () => { pending.resolve(empty()) }); expect(await fixture.baskets.load(fixture.scope)).toEqual(before); expect(fixture.state.postedBodies).toHaveLength(0)
  })
})
