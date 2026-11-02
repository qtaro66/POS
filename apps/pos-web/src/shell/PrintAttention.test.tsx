import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import type { PrintAttentionPage } from '../api/printAttentionContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { PrintAttentionPanel } from './PrintAttentionPanel.tsx'
import { createPrintAttention } from './printAttention.ts'

type Result = QueryResult<PrintAttentionPage>
const observedAt = '2026-11-02T04:00:00Z'
function empty(nextCursor: string | null = null): Result { return { kind: 'ok', value: { schemaVersion: 1, observedAt, totalCount: 0, items: [], nextCursor } } }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

describe('PC POS read-only print attention workspace', () => {
  it('reads only on the System page, displays provenance and uncertainty and pages without submitting', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), read = vi.fn(fixture.client.printAttention)
    render(<App client={{ ...fixture.client, printAttention: read }} />)
    await screen.findByRole('button', { name: /A04/ })
    expect(read).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const job = within(await screen.findByRole('article', { name: 'งานพิมพ์ DEMO-PRINT-1' }))
    expect(job.getByText(/ไม่ทราบผลการส่ง/)).toBeTruthy()
    expect(job.getByText(/ห้ามส่งซ้ำอัตโนมัติ/)).toBeTruthy()
    expect(job.getByText('DEMO-KITCHEN / DEMO-PRINTER')).toBeTruthy()
    expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'หน้าถัดไป' }))
    await screen.findByRole('article', { name: 'งานพิมพ์ DEMO-PRINT-2' })
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('region', { name: 'รายการงานพิมพ์ที่ต้องตรวจสอบ' })))
    expect(screen.queryByRole('article', { name: 'งานพิมพ์ DEMO-PRINT-1' })).toBeNull()
    expect(screen.queryByRole('button', { name: 'หน้าถัดไป' })).toBeNull()
    expect(read.mock.calls[1][0]).toBe('DEMO-NEXT')
    await user.click(screen.getByRole('button', { name: 'อ่านงานพิมพ์ใหม่' }))
    await screen.findByRole('article', { name: 'งานพิมพ์ DEMO-PRINT-1' })
    expect(read.mock.calls[2][0]).toBeUndefined()
    expect(screen.queryByRole('button', { name: /^(Retry|Reprint|ทดสอบ|พิมพ์)/ })).toBeNull()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('separates unsupported, unavailable and observed empty states without raw diagnostics', async () => {
    const user = userEvent.setup(), read = vi.fn(async (): Promise<Result> => { throw new Error('PRIVATE stack') })
    const view = render(<PrintAttentionPanel port={{}} />)
    expect(screen.getByText('ยังไม่เชื่อมต่อรายการงานพิมพ์')).toBeTruthy()
    view.rerender(<PrintAttentionPanel port={{ printAttention: read }} />)
    await screen.findByRole('alert')
    expect(screen.queryByText('PRIVATE stack')).toBeNull()
    expect(screen.queryByText(/งานในกลุ่มที่ต้องตรวจสอบทั้งหมด/)).toBeNull()
    read.mockResolvedValue(empty())
    await user.click(screen.getByRole('button', { name: 'อ่านงานพิมพ์ใหม่' }))
    await screen.findByText(/ไม่พบงานในหน้าที่อ่าน/)
    expect(screen.getByText(/ไม่ใช่การยืนยันว่าเครื่องพิมพ์ทุกเครื่องปกติ/)).toBeTruthy()
  })

  it('clears the current page during refresh and ignores stale page completions', async () => {
    const stale = deferred<Result>(), read = vi.fn().mockReturnValueOnce(stale.promise).mockResolvedValue(empty())
    const controller = createPrintAttention({ printAttention: read })
    const old = controller.refresh()
    await controller.refresh()
    expect((read.mock.calls[0][1] as AbortSignal).aborted).toBe(true)
    stale.resolve({ kind: 'unavailable', reason: 'network' }); await old
    expect(controller.getSnapshot().phase).toBe('ready')
    expect(controller.getSnapshot().page?.totalCount).toBe(0)
  })

  it('rejects a repeated pagination cursor or a changed observation rather than silently mixing pages', async () => {
    const fixture = createOrderingPreview(), first = await fixture.client.printAttention()
    if (first.kind !== 'ok') throw new Error('Invalid fixture')
    for (const second of [first, { ...first, value: { ...first.value, observedAt, nextCursor: null } },
      { ...first, value: { ...first.value, observedAt: first.value.observedAt.replace('Z', '.0000001Z'), nextCursor: null } }]) {
      const read = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce(second)
      const controller = createPrintAttention({ printAttention: read })
      await controller.refresh(); await controller.next()
      expect(controller.getSnapshot().phase).toBe('unavailable')
      expect(controller.getSnapshot().page).toBeNull()
    }
  })

  it('accepts equivalent UTC offset/fraction representations without losing sub-millisecond observation precision', async () => {
    const fixture = createOrderingPreview(), first = await fixture.client.printAttention()
    if (first.kind !== 'ok') throw new Error('Invalid fixture')
    const read = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce({ ...first,
      value: { ...first.value, observedAt: first.value.observedAt.replace('Z', '.0000000+00:00'), nextCursor: null } })
    const controller = createPrintAttention({ printAttention: read })
    await controller.refresh(); await controller.next()
    expect(controller.getSnapshot().phase).toBe('ready')
    expect(controller.getSnapshot().pageNumber).toBe(2)
  })

  it('ignores a pending page on identity replacement and cancels reads on lock', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    const view = render(<App client={{ ...fixture.client, printAttention: async (_, value) => { signal = value; return pending.promise } }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const oldSignal = signal
    view.rerender(<App client={{ ...fixture.client, printAttention: async () => empty() }} />)
    await screen.findByText(/ไม่พบงานในหน้าที่อ่าน/)
    expect(oldSignal?.aborted).toBe(true)
    await act(async () => { pending.resolve({ kind: 'unavailable', reason: 'network' }) })
    expect(screen.queryByRole('alert')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
    expect(screen.queryByRole('heading', { name: 'งานพิมพ์ที่ต้องตรวจสอบ' })).toBeNull()
  })

  it('cancels pending reads when leaving System and keeps basket content unchanged', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    const read = vi.fn(async (_: string | undefined, value?: AbortSignal) => { signal = value; return pending.promise })
    render(<App client={{ ...fixture.client, printAttention: read }} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const before = await fixture.baskets.load(fixture.scope)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    expect(signal?.aborted).toBe(true)
    await screen.findByRole('button', { name: 'เพิ่มจำนวน น้ำเปล่า' })
    await act(async () => { pending.resolve(empty()) })
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
    expect(fixture.state.postedBodies).toHaveLength(0)
  })
})
