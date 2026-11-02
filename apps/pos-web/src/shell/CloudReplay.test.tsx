import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import type { CloudReplayPage } from '../api/cloudReplayContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { CloudReplayPanel } from './CloudReplayPanel.tsx'
import { createCloudReplayStatus } from './cloudReplayStatus.ts'

type Result = QueryResult<CloudReplayPage>
const empty = (): Result => ({ kind: 'ok', value: { schemaVersion: 1, observedAt: '2026-11-02T10:00:00Z',
  summary: { pendingCount: 0, leasedCount: 0, unleasedCount: 0, withRetriesCount: 0 }, items: [], nextAfterSequence: null } })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

describe('PC POS read-only replay queue', () => {
  it('reads only in System, explains separate overlapping counts and pages exact sequences without replaying', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), read = vi.fn(fixture.client.cloudReplayStatus)
    render(<App client={{ ...fixture.client, cloudReplayStatus: read }} />)
    await screen.findByRole('button', { name: /A04/ }); expect(read).not.toHaveBeenCalled()
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const first = within(await screen.findByRole('article', { name: 'Recovery DEMO-RECOVERY-1' }))
    expect(first.getByRole('heading', { name: /9007199254740993.*9007199254740995/ })).toBeTruthy()
    expect(screen.getByText(/คิวนี้แยกจาก Outbox หลักและไม่รวมยอดเข้าด้วยกัน/)).toBeTruthy()
    expect(screen.getByText(/กลุ่มที่เคยล้มเหลวซ้อนกับกลุ่มมี\/ไม่มี Lease/)).toBeTruthy()
    expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
    await user.click(screen.getByRole('button', { name: 'คิวกู้คืนหน้าถัดไป' }))
    const second = within(await screen.findByRole('article', { name: 'Recovery DEMO-RECOVERY-2' }))
    expect(second.getByText('มี Lease (อาจหมดอายุแล้ว)')).toBeTruthy()
    await waitFor(() => expect(document.activeElement).toBe(screen.getByRole('region', { name: 'รายการคิวกู้คืน' })))
    expect(read.mock.calls[1][0]).toBe('9007199254740993')
    expect(screen.queryByRole('article', { name: 'Recovery DEMO-RECOVERY-1' })).toBeNull()
    await user.click(screen.getByRole('button', { name: 'อ่านคิวกู้คืนใหม่' }))
    await screen.findByRole('article', { name: 'Recovery DEMO-RECOVERY-1' }); expect(read.mock.calls[2][0]).toBeUndefined()
    expect(screen.queryByRole('button', { name: /^(Retry|Replay|ยืนยันรับ|ปล่อย Lease)/ })).toBeNull()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('separates unsupported, unavailable and observed-empty queues with no raw errors', async () => {
    const user = userEvent.setup(), read = vi.fn(async (): Promise<Result> => { throw new Error('PRIVATE'); })
    const view = render(<CloudReplayPanel port={{}} />)
    expect(screen.getByText('ยังไม่เชื่อมต่อคิวกู้คืน')).toBeTruthy()
    view.rerender(<CloudReplayPanel port={{ cloudReplayStatus: read }} />)
    await screen.findByRole('alert'); expect(screen.queryByText('PRIVATE')).toBeNull(); expect(screen.queryByText('คำขอที่ยังไม่เสร็จ')).toBeNull()
    read.mockResolvedValue(empty()); await user.click(screen.getByRole('button', { name: 'อ่านคิวกู้คืนใหม่' }))
    await screen.findByText(/ไม่พบคำขอในหน้าที่อ่าน/); expect(screen.getByText(/ไม่ยืนยันว่าระบบ Cloud ทั้งหมดปกติ/)).toBeTruthy()
  })

  it('clears previous counts while loading and rejects late completions after refresh', async () => {
    const pending = deferred<Result>(), fixture = createOrderingPreview(), first = await fixture.client.cloudReplayStatus()
    const read = vi.fn().mockResolvedValueOnce(first).mockReturnValueOnce(pending.promise).mockResolvedValueOnce(empty())
    const controller = createCloudReplayStatus({ cloudReplayStatus: read })
    await controller.refresh(); const old = controller.refresh(); expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect((read.mock.calls[1][1] as AbortSignal).aborted).toBe(true)
    pending.resolve(first); await old; expect(controller.getSnapshot().page?.summary.pendingCount).toBe(0)
  })

  it('does not display a failed page as zero work and refreshes from the first page', async () => {
    const fixture = createOrderingPreview(), first = await fixture.client.cloudReplayStatus()
    const read = vi.fn().mockResolvedValueOnce(first).mockResolvedValueOnce({ kind: 'unavailable', reason: 'http', httpStatus: 503 }).mockResolvedValueOnce(empty())
    const controller = createCloudReplayStatus({ cloudReplayStatus: read })
    await controller.refresh(); await controller.next(); expect(controller.getSnapshot().phase).toBe('unavailable'); expect(controller.getSnapshot().page).toBeNull()
    await controller.refresh(); expect(read.mock.calls[2][0]).toBeUndefined(); expect(controller.getSnapshot().pageNumber).toBe(1)
  })

  it('does not leak a pending observation into a replacement identity or the lock screen', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    const view = render(<App client={{ ...fixture.client, cloudReplayStatus: async (_, value) => { signal = value; return pending.promise } }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' })); const previous = signal
    view.rerender(<App client={{ ...fixture.client, cloudReplayStatus: async () => empty() }} />)
    await screen.findByText(/ไม่พบคำขอในหน้าที่อ่าน/); expect(previous?.aborted).toBe(true)
    await act(async () => { pending.resolve({ kind: 'unavailable', reason: 'network' }) }); expect(screen.queryByRole('alert')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' })); expect(screen.queryByRole('heading', { name: 'คิวกู้คืน Sequence' })).toBeNull()
  })

  it('cancels when leaving System and preserves the durable basket without command writes', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup(), pending = deferred<Result>()
    let signal: AbortSignal | undefined
    render(<App client={{ ...fixture.client, cloudReplayStatus: async (_, value) => { signal = value; return pending.promise } }} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ })); await user.click(screen.getByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' })); await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull()); const before = await fixture.baskets.load(fixture.scope)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' })); await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    await screen.findByRole('button', { name: 'เพิ่มจำนวน น้ำเปล่า' }); expect(signal?.aborted).toBe(true)
    await act(async () => { pending.resolve(empty()) }); expect(await fixture.baskets.load(fixture.scope)).toEqual(before); expect(fixture.state.postedBodies).toHaveLength(0)
  })
})
