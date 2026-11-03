import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import type { CloudOutboxPage } from '../api/cloudOutboxContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { CloudOutboxPanel } from './CloudOutboxPanel.tsx'
import { createCloudOutboxStatus } from './cloudOutboxStatus.ts'

type Result = QueryResult<CloudOutboxPage>
const empty = (): Result => ({ kind: 'ok', value: { schemaVersion: 1, observedAt: '2026-11-02T09:00:00Z',
  summary: { unacknowledgedCount: 0, pendingCount: 0, processingCount: 0, failedCount: 0, otherCount: 0 }, items: [], nextAfterSequence: null } })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

it('shows branch cloud observations on Home and focuses the detailed outbox', async () => {
  const fixture = createOrderingPreview(), user = userEvent.setup()
  render(<App client={fixture.client} />)
  await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
  const summary = within(screen.getByRole('region', { name: 'ข้อมูลส่วนกลาง' }))
  await summary.findByText('2')
  expect(summary.getByText('ซิงก์ข้อมูลพนักงานล่าสุด').nextElementSibling?.querySelector('time')?.dateTime).toBe('2026-11-01T08:45:00Z')
  expect(summary.getByText('ส่วนกลางยืนยันรับล่าสุด').nextElementSibling?.querySelector('time')?.dateTime).toBe('2026-11-01T09:00:00Z')
  expect(summary.getByText(/สถานะการเชื่อมต่อส่วนกลางขณะนี้ยังไม่ทราบ/)).toBeTruthy()
  await user.click(summary.getByRole('button', { name: 'เปิดรายละเอียด Outbox' }))
  expect(screen.getByRole('heading', { name: 'ข้อมูลรอส่วนกลางยืนยันรับ' })).toBe(document.activeElement)
  expect(fixture.state.postedBodies).toHaveLength(0)
})

describe('PC POS read-only cloud outbox workspace', () => {
  it('reads only in System, preserves sequence digits, pages and refreshes without sending', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), read = vi.fn(fixture.client.cloudOutboxStatus)
    render(<App client={{ ...fixture.client, cloudOutboxStatus: read }} />)
    await screen.findByRole('button', { name: /A04/ }); expect(read).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const first = within(await screen.findByRole('article', { name: 'Outbox DEMO-EVENT-1' }))
    expect(first.getByRole('heading', { name: /9007199254740993/ })).toBeTruthy()
    expect(first.getByText(/คาดหวัง: 9007199254740992/)).toBeTruthy()
    expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'Outbox หน้าถัดไป' }))
    await screen.findByRole('article', { name: 'Outbox DEMO-EVENT-2' })
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('region', { name: 'รายการ Outbox' })))
    expect(read.mock.calls[1][0]).toBe('9007199254740993')
    expect(screen.queryByRole('article', { name: 'Outbox DEMO-EVENT-1' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'อ่าน Outbox ใหม่' }))
    await screen.findByRole('article', { name: 'Outbox DEMO-EVENT-1' })
    expect(read.mock.calls[2][0]).toBeUndefined()
    expect(screen.queryByRole('button', { name: /^(Retry|Replay|ส่งข้อมูล)/ })).toBeNull()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('separates unsupported, failed and observed empty data without exposing raw errors', async () => {
    const user = userEvent.setup(), read = vi.fn(async (): Promise<Result> => { throw new Error('PRIVATE'); })
    const view = render(<CloudOutboxPanel port={{}} />)
    expect(screen.getByText('ยังไม่เชื่อมต่อข้อมูล Outbox')).toBeTruthy()
    view.rerender(<CloudOutboxPanel port={{ cloudOutboxStatus: read }} />)
    await screen.findByRole('alert'); expect(screen.queryByText('PRIVATE')).toBeNull()
    expect(screen.queryByText('ยังไม่ยืนยันรับทั้งหมด')).toBeNull()
    read.mockResolvedValue(empty())
    await user.click(screen.getByRole('button', { name: 'อ่าน Outbox ใหม่' }))
    await screen.findByText(/ไม่พบรายการในหน้าที่อ่าน/)
    expect(screen.getByText(/ไม่ใช่การยืนยันว่าส่วนกลางออนไลน์/)).toBeTruthy()
  })

  it('hides old counts during refresh and ignores late completions', async () => {
    const pending = deferred<Result>(), fixture = createOrderingPreview(), first = await fixture.client.cloudOutboxStatus()
    const read = vi.fn().mockResolvedValueOnce(first).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(empty())
    const controller = createCloudOutboxStatus({ cloudOutboxStatus: read })
    await controller.refresh()
    const old = controller.refresh()
    expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect((read.mock.calls[1][1] as AbortSignal).aborted).toBe(true)
    pending.resolve(first); await old
    expect(controller.getSnapshot().page?.summary.unacknowledgedCount).toBe(0)
  })

  it('hides failed page results and starts again from page one on explicit refresh', async () => {
    const fixture = createOrderingPreview(), first = await fixture.client.cloudOutboxStatus()
    const read = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce({ kind: 'unavailable', reason: 'http', httpStatus: 503 }).mockResolvedValueOnce(empty())
    const controller = createCloudOutboxStatus({ cloudOutboxStatus: read })
    await controller.refresh(); await controller.next()
    expect(controller.getSnapshot().phase).toBe('unavailable'); expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect(read.mock.calls[2][0]).toBeUndefined(); expect(controller.getSnapshot().pageNumber).toBe(1)
  })

  it('cancels on identity replacement and screen lock without restoring old observations', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    const view = render(<App client={{ ...fixture.client, cloudOutboxStatus: async (_, value) => { signal = value; return pending.promise } }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const previousSignal = signal
    view.rerender(<App client={{ ...fixture.client, cloudOutboxStatus: async () => empty() }} />)
    await screen.findByText(/ไม่พบรายการในหน้าที่อ่าน/); expect(previousSignal?.aborted).toBe(true)
    await act(async () => { pending.resolve({ kind: 'unavailable', reason: 'network' }) })
    expect(screen.queryByRole('alert')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
    expect(screen.queryByRole('heading', { name: 'ข้อมูลรอส่วนกลางยืนยันรับ' })).toBeNull()
  })

  it('preserves basket rows and cancels outbox reads when returning to ordering', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    render(<App client={{ ...fixture.client, cloudOutboxStatus: async (_, value) => { signal = value; return pending.promise } }} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ })); await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })); await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const before = await fixture.baskets.load(fixture.scope)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' })); await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    await screen.findByRole('button', { name: 'เพิ่มจำนวน น้ำเปล่า' }); expect(signal?.aborted).toBe(true)
    await act(async () => { pending.resolve(empty()) })
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before); expect(fixture.state.postedBodies).toHaveLength(0)
  })
})
