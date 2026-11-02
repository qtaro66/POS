import { StrictMode } from 'react'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createOrderingPreview } from '../ordering/orderingPreview.ts'
import { createDataFreshness } from './dataFreshness.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const ok = (value: string | null): QueryResult<string | null> => ({ kind: 'ok', value })
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

describe('PC POS data freshness', () => {
  it('keeps the confirmed sidebar labels on all pages, explains narrow provenance and never claims overall health', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    render(<StrictMode><App client={fixture.client} preview /></StrictMode>)
    const sidebar = within(screen.getByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' }))
    await waitFor(() => expect(sidebar.getByText('Data Synced').nextElementSibling?.getAttribute('datetime')).toBe('2026-11-01T08:45:00Z'))
    expect(sidebar.getByText('Data Sent').nextElementSibling?.getAttribute('datetime')).toBe('2026-11-01T09:00:00Z')
    expect(sidebar.getByText('ข้อมูลพนักงานเท่านั้น')).toBeTruthy()
    for (const page of ['หน้าหลัก', 'ประวัติธุรกรรม', 'ร้านและกะ', 'สถานะระบบ', 'โต๊ะ']) {
      await user.click(screen.getByRole('button', { name: page }))
      expect(sidebar.getByText('Data Sent')).toBeTruthy()
    }
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
    expect(screen.getByText(/ยังไม่มีข้อมูล Health Check/)).toBeTruthy()
    expect(screen.queryByRole('button', { name: /Retry|Reprint|PIN/ })).toBeNull()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('handles never-recorded and failed reads separately, refreshes explicitly and exposes no raw errors', async () => {
    const user = userEvent.setup(), fixture = createOrderingPreview()
    const dataSyncStatus = vi.fn(async () => ok(null))
    const dataSentStatus = vi.fn(async (): Promise<QueryResult<string | null>> => { throw new Error('private diagnostic') })
    render(<App client={{ ...fixture.client, dataSyncStatus, dataSentStatus }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    const main = within(screen.getByRole('main'))
    await main.findByText('ยังไม่มีบันทึก'); await main.findByText('อ่านข้อมูลไม่ได้')
    expect(screen.queryByText('private diagnostic')).toBeNull()
    dataSentStatus.mockResolvedValue(ok('2026-11-02T03:00:00Z'))
    await user.click(main.getByRole('button', { name: 'อ่านสถานะใหม่' }))
    await waitFor(() => expect(main.queryByText('อ่านข้อมูลไม่ได้')).toBeNull())
    expect(dataSyncStatus).toHaveBeenCalledTimes(2); expect(dataSentStatus).toHaveBeenCalledTimes(2)
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('shows unsupported capabilities without pretending they are offline or empty', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    render(<App client={{ activeTables: fixture.client.activeTables, sessionDetail: fixture.client.sessionDetail }} />)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    expect(within(screen.getByRole('main')).getAllByText('ยังไม่เชื่อมต่อ')).toHaveLength(2)
    expect(screen.queryByText('ยังไม่มีบันทึก')).toBeNull()
  })

  it('preserves the durable basket across status navigation and never submits an order while refreshing status', async () => {
    const fixture = createOrderingPreview(), user = userEvent.setup()
    render(<App client={fixture.client} ordering={{ factory: fixture.factory }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: 'เปิดหน้ารายการ' }))
    await user.click(await screen.findByRole('button', { name: 'เลือกเมนู น้ำเปล่า' }))
    await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ลงตะกร้า' }))
    await waitFor(() => expect(screen.queryByRole('dialog')).toBeNull())
    const before = await fixture.baskets.load(fixture.scope)
    await user.click(screen.getByRole('button', { name: 'สถานะระบบ' }))
    await user.click(screen.getByRole('button', { name: 'อ่านสถานะใหม่' }))
    await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    await screen.findByRole('button', { name: 'เพิ่มจำนวน น้ำเปล่า' })
    expect(await fixture.baskets.load(fixture.scope)).toEqual(before)
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('clears previous timestamps during refresh and prevents late responses from undoing a newer observation', async () => {
    const stale = deferred<QueryResult<string | null>>()
    const read = vi.fn().mockResolvedValueOnce(ok('2026-11-01T00:00:00Z')).mockReturnValueOnce(stale.promise).mockResolvedValueOnce(ok(null))
    const controller = createDataFreshness({ dataSyncStatus: read })
    await controller.refresh()
    expect(controller.getSnapshot().synced).toEqual({ phase: 'ready', timestamp: '2026-11-01T00:00:00Z' })
    const earlier = controller.refresh()
    expect(controller.getSnapshot().synced).toEqual({ phase: 'loading' })
    const oldSignal = read.mock.calls[1][0] as AbortSignal
    await controller.refresh()
    expect(oldSignal.aborted).toBe(true)
    stale.resolve(ok('2026-11-01T00:00:00Z')); await earlier
    expect(controller.getSnapshot().synced).toEqual({ phase: 'ready', timestamp: null })
  })

  it('renders successful reads independently while the other query is pending', async () => {
    const pending = deferred<QueryResult<string | null>>()
    const controller = createDataFreshness({ dataSyncStatus: async () => ok(null), dataSentStatus: () => pending.promise })
    const reading = controller.refresh()
    await Promise.resolve(); await Promise.resolve()
    expect(controller.getSnapshot().synced).toEqual({ phase: 'ready', timestamp: null })
    expect(controller.getSnapshot().sent).toEqual({ phase: 'loading' })
    pending.resolve({ kind: 'unavailable', reason: 'http', httpStatus: 503 }); await reading
    expect(controller.getSnapshot().sent).toEqual({ phase: 'unavailable' })
  })

  it('cancels observation on screen lock and ignores its late result', async () => {
    const pending = deferred<QueryResult<string | null>>(), fixture = createOrderingPreview(), user = userEvent.setup()
    let signal: AbortSignal | undefined
    render(<App client={{ ...fixture.client, dataSyncStatus: async value => { signal = value; return pending.promise } }} />)
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
    expect(signal?.aborted).toBe(true)
    await act(async () => { pending.resolve(ok('2026-11-02T03:00:00Z')) })
    expect(screen.queryByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' })).toBeNull()
    expect(screen.queryByText('Data Synced')).toBeNull()
  })

  it('never carries status from one authenticated client into a replacement client', async () => {
    const fixture = createOrderingPreview(), old = deferred<QueryResult<string | null>>()
    const view = render(<App client={{ ...fixture.client, dataSyncStatus: () => old.promise }} />)
    view.rerender(<App client={{ ...fixture.client, dataSyncStatus: async () => ok(null), dataSentStatus: async () => ok(null) }} />)
    const sidebar = within(screen.getByRole('region', { name: 'สถานะข้อมูลส่วนกลาง' }))
    await waitFor(() => expect(sidebar.getAllByText('ยังไม่มีบันทึก')).toHaveLength(2))
    await act(async () => { old.resolve(ok('2026-11-02T03:00:00Z')) })
    expect(sidebar.getAllByText('ยังไม่มีบันทึก')).toHaveLength(2)
    expect(document.querySelector('time[datetime="2026-11-02T03:00:00Z"]')).toBeNull()
  })
})
