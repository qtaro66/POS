import { StrictMode } from 'react'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import App from '../App.tsx'
import { createPreviewReadClient, previewDetail } from './preview.ts'
import type { OrderingReadClient } from './readController.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import type { SessionDetail } from '../api/orderingContracts.ts'
import { strings as s } from './strings.ts'

describe('PC POS read-only sent orders', () => {
  it('requires an authenticated client before rendering tables or financial data', () => {
    render(<App />)
    expect(screen.getByRole('heading', { name: s.signInTitle })).toBeTruthy()
    expect(screen.queryByTestId('net-total')).toBeNull()
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
  })

  it('renders accepted rounds, fulfillment groups, options and authoritative totals', async () => {
    const user = userEvent.setup()
    render(<App client={createPreviewReadClient()} branchLabel="สาขาทดสอบ" preview />)
    expect(screen.getByText(s.preview)).toBeTruthy()
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    expect(await screen.findByText('ข้าวผัดกุ้ง')).toBeTruthy()
    expect(screen.getAllByText(s.accepted)).toHaveLength(2)
    expect(screen.getByText(/ไข่ดาว/)).toBeTruthy()
    expect(screen.getByText(/ไม่ใส่ต้นหอม/)).toBeTruthy()
    expect(screen.getAllByRole('region', { name: s.dineIn })).toHaveLength(2)
    expect(screen.getByRole('region', { name: s.takeaway })).toBeTruthy()
    expect(screen.getByText(s.cancelled)).toBeTruthy()
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    expect(screen.queryByRole('button', { name: /^(ส่งออเดอร์|รับชำระ|ยกเลิกรายการ|พิมพ์)/ })).toBeNull()
  })

  it('hides stale totals immediately on refresh and keeps them hidden after an error', async () => {
    const user = userEvent.setup(), preview = createPreviewReadClient()
    let fail = false
    let resolve!: (value: QueryResult<SessionDetail>) => void
    const waiting = new Promise<QueryResult<SessionDetail>>(done => { resolve = done })
    const client: OrderingReadClient = { ...preview, sessionDetail: async id => fail ? waiting : preview.sessionDetail(id) }
    render(<App client={client} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    fail = true
    await user.click(screen.getByRole('button', { name: s.refresh }))
    expect(screen.queryByTestId('net-total')).toBeNull()
    await act(async () => { resolve({ kind: 'unavailable', reason: 'network' }) })
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', s.detailError)
    expect(screen.queryByTestId('net-total')).toBeNull()
  })

  it('does not flash old branch data when the authenticated client changes', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByText('ข้าวผัดกุ้ง')
    const pending = new Promise<Awaited<ReturnType<OrderingReadClient['activeTables']>>>(() => {})
    rerender(<App client={{ activeTables: async () => pending, sessionDetail: async () => ({ kind: 'notFound' }) }} />)
    expect(screen.queryByText('ข้าวผัดกุ้ง')).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
  })

  it('manual screen lock hides table data and the same client cannot unlock it', async () => {
    const user = userEvent.setup(), client = createPreviewReadClient()
    const { rerender } = render(<App client={client} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    await user.click(screen.getByRole('button', { name: s.lock }))
    expect(screen.getByRole('heading', { name: s.signInTitle })).toBeTruthy()
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: s.signInTitle }))
    expect(screen.queryByTestId('net-total')).toBeNull()
    rerender(<App client={client} />)
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    rerender(<App />)
    rerender(<App client={client} />)
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    rerender(<App client={createPreviewReadClient()} />)
    expect(await screen.findByRole('button', { name: /A04/ })).toBeTruthy()
  })

  it('shows empty sessions without inventing a bill and handles missing session detail', async () => {
    const user = userEvent.setup(), preview = createPreviewReadClient()
    render(<App client={{ ...preview, sessionDetail: async id => id === 'S-A04' ? { kind: 'notFound' } : preview.sessionDetail(id) }} />)
    await user.click(await screen.findByRole('button', { name: /A08/ }))
    expect(await screen.findByText(s.noOrders)).toBeTruthy()
    expect(screen.getByText(s.noActiveBill)).toBeTruthy()
    expect(screen.queryByTestId('net-total')).toBeNull()
    await user.click(screen.getByRole('button', { name: /A04/ }))
    expect(await screen.findByText(s.notFound)).toBeTruthy()
  })

  it('works through StrictMode effect cleanup and remains keyboard selectable', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    const read = vi.fn(base.activeTables)
    render(<StrictMode><App client={{ ...base, activeTables: read }} /></StrictMode>)
    const button = await screen.findByRole('button', { name: /A04/ })
    button.focus(); await user.keyboard('{Enter}')
    await screen.findByTestId('net-total')
    expect(button.getAttribute('aria-pressed')).toBe('true')
    expect(read.mock.calls.length).toBeGreaterThanOrEqual(1)
    expect(within(screen.getByRole('region', { name: 'สรุปยอดจากสาขา' })).getByText(s.notReceipt)).toBeTruthy()
  })

  it('reports table-read failure without displaying stale sessions or totals', async () => {
    render(<App client={{ activeTables: async () => { throw new Error('synthetic failure') }, sessionDetail: async () => ({ kind: 'ok', value: previewDetail() }) }} />)
    await waitFor(() => expect(screen.getAllByRole('alert').length).toBeGreaterThan(0))
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
  })
})
