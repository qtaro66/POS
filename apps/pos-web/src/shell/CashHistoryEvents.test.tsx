import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryEvents } from './CashHistoryEvents.tsx'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import type { QueryResult } from '../api/orderingClient.ts'
import type { CashHistoryEventPage, CashHistoryEventQuery } from '../api/cashHistoryEventContracts.ts'
import { cancellationEvidence, eventPage, sessionEvidence } from '../api/cashHistoryEventTestFixtures.ts'
import { cashDetail, cashItem, cashPage } from '../api/cashHistoryTestFixtures.ts'

const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const reader = () => vi.fn(async (_request: string, query: CashHistoryEventQuery = {}, _signal?: AbortSignal): Promise<QueryResult<CashHistoryEventPage>> => ok(eventPage(query.source)))
const props = { branchId: 'branch', requestId: 'CASH-1', tableSessionId: 'SESSION-1' }

it('releases interrupted continuation state when its reader is replaced', async () => {
  let finish!: (value: QueryResult<CashHistoryEventPage>) => void
  let pendingSignal: AbortSignal | undefined
  const oldRead = reader().mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence('old')], 'oldcursor')))
    .mockImplementationOnce((_id, _query, signal) => { pendingSignal = signal; return new Promise(resolve => { finish = resolve }) })
  const view = render(<CashHistoryEvents {...props} read={oldRead} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านหลักฐานเพิ่มเติม' }))
  const newRead = reader().mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence('new')], 'newcursor')))
    .mockResolvedValue(ok(eventPage('Session', [sessionEvidence('new-next')])))
  view.rerender(<CashHistoryEvents {...props} read={newRead} />)
  const more = await screen.findByRole('button', { name: 'อ่านหลักฐานเพิ่มเติม' })
  expect(pendingSignal?.aborted).toBe(true)
  expect((more as HTMLButtonElement).disabled).toBe(false)
  fireEvent.click(more)
  await waitFor(() => expect(newRead).toHaveBeenCalledTimes(2))
  await act(async () => { finish(ok(eventPage('Session', [sessionEvidence('obsolete')]))); await Promise.resolve() })
  expect(screen.queryByText(/obsolete/)).toBeNull()
})

it('reads independent sources and shows only recorded actors, table changes and cancellation quantities', async () => {
  const read = reader()
  render(<CashHistoryEvents {...props} read={read} />)
  expect(await screen.findByText(/รหัสโต๊ะเดิม A01 → A02/)).toBeTruthy()
  expect(screen.getByText(/เหตุผลที่บันทึก/).textContent).toContain('เหตุผลเดิม\nบรรทัดสอง')
  expect(read).toHaveBeenLastCalledWith('CASH-1', { source: 'Session', limit: 20 }, expect.any(AbortSignal))
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  expect(await screen.findByText('ยืนยันยกเลิกบางส่วน')).toBeTruthy()
  expect(screen.getByText(/ขอยกเลิก 4 · ยังไม่ได้ยกเลิกในหลักฐานนี้ 3/)).toBeTruthy()
  expect(screen.getByText(/หลักฐานนี้ไม่ได้บันทึกอุปกรณ์ผู้ยืนยัน/)).toBeTruthy()
  expect(screen.queryByText(/recorded-device/)).toBeNull()
  expect(screen.queryByRole('button', { name: /^Refund$|^ยกเลิก$|^พิมพ์|^ส่งใหม่/ })).toBeNull()
})
it('does not turn a missing legacy read capability into an empty successful evidence list', () => {
  render(<CashHistoryEvents {...props} read={undefined} />)
  expect(screen.getByText('ยังไม่เชื่อมต่อหลักฐานเหตุการณ์จากบริการสาขารุ่นนี้')).toBeTruthy()
  expect(screen.queryByText(/ไม่พบหลักฐานจากแหล่งนี้/)).toBeNull()
})
it('cancels a late source page on tab change, including when transport ignores abort', async () => {
  let finish!: (value: QueryResult<CashHistoryEventPage>) => void, signal: AbortSignal | undefined
  const read = reader().mockImplementationOnce((_id, _query, requestSignal) => { signal = requestSignal; return new Promise(resolve => { finish = resolve }) })
  render(<CashHistoryEvents {...props} read={read} />)
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  await screen.findByText('ยืนยันยกเลิกบางส่วน')
  expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(eventPage())); await Promise.resolve() })
  expect(screen.queryByText(/รหัสโต๊ะเดิม A01/)).toBeNull()
})
it('keeps cursor/source/subject on continuation and restarts first page on refresh', async () => {
  const read = reader().mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence('first')], 'cursor')))
    .mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence('next')]))).mockResolvedValue(ok(eventPage('Session', [])))
  render(<CashHistoryEvents {...props} read={read} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านหลักฐานเพิ่มเติม' }))
  await waitFor(() => expect(screen.getAllByText(/คำขอ OPEN-1/)).toHaveLength(2))
  expect(read).toHaveBeenNthCalledWith(2, 'CASH-1', { source: 'Session', limit: 20, cursor: 'cursor' }, expect.any(AbortSignal))
  fireEvent.click(screen.getByRole('button', { name: 'อ่านหลักฐานใหม่' }))
  await screen.findByText('ไม่พบหลักฐานจากแหล่งนี้ในฐานข้อมูลสาขาที่อ่านได้')
  expect(read).toHaveBeenLastCalledWith('CASH-1', { source: 'Session', limit: 20 }, expect.any(AbortSignal))
})
it('rejects duplicate or wrong-subject continuation instead of mixing recorded evidence', async () => {
  const read = reader().mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence()], 'cursor')))
    .mockResolvedValueOnce(ok(eventPage()))
  render(<CashHistoryEvents {...props} read={read} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านหลักฐานเพิ่มเติม' }))
  expect(await screen.findByRole('alert')).toHaveProperty('textContent', 'ยังอ่านหลักฐานหน้าถัดไปไม่ได้ กรุณาอ่านหลักฐานใหม่')
  expect(screen.getAllByText(/คำขอ OPEN-1/)).toHaveLength(1)
})
it('cancels continuation on source change and cannot restore previous evidence', async () => {
  let finish!: (value: QueryResult<CashHistoryEventPage>) => void, signal: AbortSignal | undefined
  const read = reader().mockResolvedValueOnce(ok(eventPage('Session', [sessionEvidence()], 'cursor')))
    .mockImplementationOnce((_id, _query, requestSignal) => { signal = requestSignal; return new Promise(resolve => { finish = resolve }) })
  render(<CashHistoryEvents {...props} read={read} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านหลักฐานเพิ่มเติม' }))
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  await screen.findByText('ยืนยันยกเลิกบางส่วน'); expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(eventPage('Session', [sessionEvidence('late')]))); await Promise.resolve() })
  expect(screen.queryByText(/OPEN-1/)).toBeNull()
})
it('keeps immutable cash detail visible on evidence failure without any mutation or local storage change', async () => {
  const before = JSON.stringify(Object.entries(localStorage)), item = cashItem()
  const port = { cashHistory: vi.fn(async () => ok(cashPage([item]))), cashHistoryDetail: vi.fn(async () => ok(cashDetail(item))),
    cashHistoryEvents: reader().mockResolvedValue({ kind: 'unavailable', reason: 'network' }) }
  render(<CashHistoryWorkspace port={port} scopeKey="branch" />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  const detail = within(await screen.findByRole('region', { name: 'รายละเอียดธุรกรรมเงินสด' }))
  await screen.findByText('ยังอ่านหลักฐานเหตุการณ์ไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  expect(detail.getAllByText('110.00 บาท')).toHaveLength(2)
  expect(JSON.stringify(Object.entries(localStorage))).toBe(before)
  expect(screen.queryByRole('button', { name: /^Cancel$|^Refund$|^Reprint$/ })).toBeNull()
})
it('does not let a foreign session page masquerade as empty or successful evidence', async () => {
  const page = eventPage(); page.tableSessionId = 'foreign-session'
  render(<CashHistoryEvents {...props} read={reader().mockResolvedValue(ok(page))} />)
  expect(await screen.findByRole('alert')).toBeTruthy()
  expect(screen.queryByText(/รหัสโต๊ะเดิม A01/)).toBeNull()
})
it('clears old evidence and cancels the reader when a selected cash subject unmounts', async () => {
  let signal: AbortSignal | undefined
  const read = reader().mockImplementation((_id, _query, requestSignal) => { signal = requestSignal; return new Promise(() => {}) })
  const view = render(<CashHistoryEvents {...props} read={read} />)
  await waitFor(() => expect(read).toHaveBeenCalled())
  view.unmount(); expect(signal?.aborted).toBe(true)
})
it('shows empty source success separately from unavailable and preserves source on repeated refresh', async () => {
  const read = reader().mockResolvedValue(ok(eventPage('Session', [])))
  render(<CashHistoryEvents {...props} read={read} />)
  await screen.findByText('ไม่พบหลักฐานจากแหล่งนี้ในฐานข้อมูลสาขาที่อ่านได้')
  read.mockResolvedValue(ok(eventPage('Cancellation', [cancellationEvidence()])))
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  await screen.findByText('ยืนยันยกเลิกบางส่วน')
  fireEvent.click(screen.getByRole('button', { name: 'อ่านหลักฐานใหม่' }))
  await screen.findByText('ยืนยันยกเลิกบางส่วน')
  expect(read).toHaveBeenLastCalledWith('CASH-1', { source: 'Cancellation', limit: 20 }, expect.any(AbortSignal))
})
