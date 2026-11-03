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
  it('opens a session workspace and returns without losing selection, changing amounts or rereading', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    const sessionDetail = vi.fn(base.sessionDetail)
    render(<App client={{ ...base, sessionDetail }} />)
    expect(screen.queryByRole('button', { name: s.openTableWorkspace })).toBeNull()
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    const open = await screen.findByRole('button', { name: s.openTableWorkspace })
    open.focus(); await user.keyboard('{Enter}')
    const title = screen.getByRole('heading', { name: s.tableWorkspace })
    expect(document.activeElement).toBe(title)
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    expect(screen.getByRole('heading', { name: s.menu })).toBeTruthy()
    expect(screen.getByText(s.menuUnavailable)).toBeTruthy()
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    const disclosure = screen.getByText(s.breakdown)
    await user.click(disclosure)
    expect(disclosure.parentElement?.hasAttribute('open')).toBe(true)
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    await user.click(disclosure)
    expect(screen.queryByRole('button', { name: /ส่งออเดอร์|ลงตะกร้า|รับชำระ/ })).toBeNull()
    await user.click(screen.getByRole('button', { name: s.backToTables }))
    expect(document.activeElement).toBe(screen.getByRole('heading', { name: s.sent }))
    expect(screen.getByRole('button', { name: /A04/ }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    expect(sessionDetail).toHaveBeenCalledTimes(1)
  })

  it('retains workspace mode across main navigation but removes it on screen lock', async () => {
    const user = userEvent.setup()
    render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: s.openTableWorkspace }))
    await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
    expect(screen.queryByTestId('net-total')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    expect(screen.getByRole('heading', { name: s.tableWorkspace })).toBeTruthy()
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    await user.click(screen.getByRole('button', { name: s.lock }))
    expect(screen.queryByRole('button', { name: s.backToTables })).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
  })

  it('does not reuse workspace mode for a replacement client even when session IDs match', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: s.openTableWorkspace }))
    rerender(<App client={createPreviewReadClient()} />)
    expect(screen.queryByRole('heading', { name: s.tableWorkspace })).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    expect(await screen.findByRole('button', { name: s.openTableWorkspace })).toBeTruthy()
    expect(screen.queryByRole('button', { name: s.backToTables })).toBeNull()
  })

  it('refreshes within the workspace without displaying stale financial data on failure', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    let fail = false
    let finish!: (value: QueryResult<SessionDetail>) => void
    const waiting = new Promise<QueryResult<SessionDetail>>(resolve => { finish = resolve })
    render(<App client={{ ...base, sessionDetail: async id => fail ? waiting : base.sessionDetail(id) }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await user.click(await screen.findByRole('button', { name: s.openTableWorkspace }))
    fail = true
    await user.click(screen.getByRole('button', { name: s.refresh }))
    expect(screen.queryByTestId('net-total')).toBeNull()
    expect(screen.getByRole('button', { name: s.backToTables })).toBeTruthy()
    await act(async () => { finish({ kind: 'unavailable', reason: 'network' }) })
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', s.detailError)
    expect(screen.queryByTestId('net-total')).toBeNull()
    expect(screen.queryByRole('button', { name: s.openTableWorkspace })).toBeNull()
  })

  it('offers only the five confirmed pages without inventing operational data or reads', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    const activeTables = vi.fn(base.activeTables), sessionDetail = vi.fn(base.sessionDetail)
    render(<App client={{ ...base, activeTables, sessionDetail }} />)
    const navigation = within(screen.getByRole('navigation', { name: 'เมนูหลัก' }))
    expect(navigation.getAllByRole('button').map(button => button.textContent?.slice(1))).toEqual([
      'หน้าหลัก', 'โต๊ะ', 'ประวัติธุรกรรม', 'ร้านและกะ', 'สถานะระบบ',
    ])
    await screen.findByRole('button', { name: /A04/ })
    for (const name of ['หน้าหลัก', 'ประวัติธุรกรรม', 'ร้านและกะ', 'สถานะระบบ']) {
      await user.click(navigation.getByRole('button', { name }))
      expect(screen.getByRole('main', { name })).toBe(document.activeElement)
      if (name === 'สถานะระบบ') expect(screen.getByText(/สถานะรวม: ยังไม่ทราบ/)).toBeTruthy()
      else if (name === 'หน้าหลัก') expect(screen.getByText('ยังไม่มีข้อมูลผังโต๊ะ')).toBeTruthy()
      else expect(screen.getByRole('status').textContent).toContain('ยังไม่พร้อมใช้งาน')
      expect(navigation.getByRole('button', { name }).getAttribute('aria-current')).toBe('page')
      expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    }
    expect(activeTables).toHaveBeenCalledTimes(1)
    expect(sessionDetail).not.toHaveBeenCalled()
  })

  it('shows active-session counts and routes from Home to the table list and System', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    render(<App client={base} />)
    await screen.findByRole('button', { name: /A04/ })
    await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
    const home = within(screen.getByRole('main', { name: 'หน้าหลัก' }))
    expect(home.getByText('กำลังสั่ง').nextElementSibling?.textContent).toBe('2')
    expect(home.getByText('รอชำระ').nextElementSibling?.textContent).toBe('1')
    expect(home.getByText('โต๊ะว่าง').nextElementSibling?.textContent).toBe('ยังไม่มีข้อมูลผังโต๊ะ')
    await user.click(home.getByRole('button', { name: 'เปิดสถานะระบบ' }))
    expect(screen.getByRole('main', { name: 'สถานะระบบ' })).toBe(document.activeElement)
    await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
    await user.click(home.getByRole('button', { name: 'เปิดรายการโต๊ะ' }))
    expect(screen.getByRole('main', { name: 'โต๊ะ' })).toBe(document.activeElement)
    expect(screen.getByRole('button', { name: /A04/ })).toBeTruthy()
  })

  it('drills into a Home status without displaying a previously selected nonmatching session', async () => {
    const user = userEvent.setup()
    render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    await user.click(screen.getByRole('button', { name: 'หน้าหลัก' }))
    await user.click(screen.getByRole('button', { name: 'ดูโต๊ะรอชำระ' }))
    expect(screen.getByRole('main', { name: 'โต๊ะ' })).toBe(document.activeElement)
    expect(screen.getByRole('button', { name: /B12/ })).toBeTruthy()
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
    await user.click(screen.getByRole('button', { name: 'แสดงโต๊ะที่เปิดอยู่ทั้งหมด' }))
    expect(screen.getByRole('button', { name: /A04/ })).toBeTruthy()
  })

  it('preserves the selected session and Branch totals across keyboard navigation without rereading', async () => {
    const user = userEvent.setup(), base = createPreviewReadClient()
    const sessionDetail = vi.fn(base.sessionDetail)
    render(<App client={{ ...base, sessionDetail }} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    screen.getByRole('button', { name: 'หน้าหลัก' }).focus()
    await user.keyboard('{Enter}')
    expect(screen.queryByTestId('net-total')).toBeNull()
    screen.getByRole('button', { name: 'โต๊ะ' }).focus()
    await user.keyboard('{Enter}')
    expect(screen.getByRole('button', { name: /A04/ }).getAttribute('aria-pressed')).toBe('true')
    expect(screen.getByTestId('net-total').textContent).toBe('530.00 บาท')
    expect(sessionDetail).toHaveBeenCalledTimes(1)
  })

  it('cannot restore another authenticated client session after navigation', async () => {
    const user = userEvent.setup()
    const { rerender } = render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    await user.click(screen.getByRole('button', { name: 'ประวัติธุรกรรม' }))
    rerender(<App client={{ activeTables: async () => new Promise(() => {}), sessionDetail: async () => ({ kind: 'notFound' }) }} />)
    await user.click(screen.getByRole('button', { name: 'โต๊ะ' }))
    expect(screen.queryByTestId('net-total')).toBeNull()
    expect(screen.queryByText('ข้าวผัดกุ้ง')).toBeNull()
    expect(screen.queryByRole('button', { name: /A04/ })).toBeNull()
  })

  it('screen lock removes navigation and selected data even from an unavailable page', async () => {
    const user = userEvent.setup()
    render(<App client={createPreviewReadClient()} />)
    await user.click(await screen.findByRole('button', { name: /A04/ }))
    await screen.findByTestId('net-total')
    await user.click(screen.getByRole('button', { name: 'ร้านและกะ' }))
    await user.click(screen.getByRole('button', { name: s.lock }))
    expect(screen.getByRole('heading', { name: s.signInTitle })).toBe(document.activeElement)
    expect(screen.queryByRole('navigation')).toBeNull()
    expect(screen.queryByTestId('net-total')).toBeNull()
  })

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
