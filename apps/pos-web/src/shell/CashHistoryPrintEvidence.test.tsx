import { act, fireEvent, render, screen, waitFor, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryPrintEvidence } from './CashHistoryPrintEvidence.tsx'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import { parseCashHistoryPrintEvidence, parseCashHistoryPrintAttempts, samePrintJobIdentity, type CashHistoryPrintJob, type CashHistoryPrintQuery, type CashHistoryPrintEvidence as Evidence, type CashHistoryPrintAttemptsPage } from '../api/cashHistoryPrintContracts.ts'
import type { CashHistoryProductionItem } from '../api/cashHistoryProductionContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { cashItemizedDetail, cashPage } from '../api/cashHistoryTestFixtures.ts'

const time = '2026-11-27T03:00:00Z'
const snapshot: CashHistoryProductionItem = { snapshotId: 'branch:submit:production:1', orderBatchId: 'ORDER-1', submittedRequestId: 'SUBMIT-1', tableSessionId: 'SESSION-1', tableId: 'A01', productionStationId: 'kitchen', submittedAt: time, lineCount: 1, orderLineIds: ['LINE-1'] }
const props = { branchId: 'branch', requestId: 'CASH-1', snapshot }
const job = (change: Partial<CashHistoryPrintJob> = {}): CashHistoryPrintJob => ({ printJobId: 'branch:submit:production-print:1', requestId: 'SUBMIT-1', snapshotId: snapshot.snapshotId, productionStationId: 'kitchen', status: 'Unknown', attemptCount: 3, createdAt: time, isReprint: false, originalPrintJobId: null, ...change })
const subject = { schemaVersion: 1 as const, branchId: 'branch', cashRequestId: 'CASH-1', tableSessionId: 'SESSION-1', snapshotId: snapshot.snapshotId, asOf: time }
const evidence = (): Evidence => ({ ...subject, job: job() })
const attempts = (): CashHistoryPrintAttemptsPage => ({ ...subject, job: job(), items: [{ printAttemptId: 'attempt3', attemptNumber: 3, status: 'Unknown', automaticRetryAllowed: false, startedAt: time, firstByteAt: time, finishedAt: time }], nextCursor: null })
const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const readEvidence = () => vi.fn(async (_request: string, _snapshot: string, _signal?: AbortSignal): Promise<QueryResult<Evidence>> => ok(evidence()))
const readAttempts = () => vi.fn(async (_request: string, _snapshot: string, _job: string, _query: CashHistoryPrintQuery = {}, _signal?: AbortSignal): Promise<QueryResult<CashHistoryPrintAttemptsPage>> => ok(attempts()))

it('validates original print identity and treats absence distinctly from unsupported or foreign evidence', () => {
  expect(parseCashHistoryPrintEvidence({ ...evidence(), job: null }, 'branch', 'CASH-1', snapshot.snapshotId, snapshot).job).toBeNull()
  for (const changed of [{ ...evidence(), branchId: 'other' }, { ...evidence(), snapshotId: 'OTHER' }, { ...evidence(), tableSessionId: 'OTHER' }, { ...evidence(), job: job({ productionStationId: 'other' }) }, { ...evidence(), job: job({ requestId: 'cash-operation' }) }, { ...evidence(), job: job({ isReprint: true, originalPrintJobId: 'original' }) }, { ...evidence(), job: job({ status: 'Unsupported' as never }) }]) expect(() => parseCashHistoryPrintEvidence(changed, 'branch', 'CASH-1', snapshot.snapshotId, snapshot)).toThrow()
})
it('accepts current job status updates while preserving original job identity', () => {
  const current = { ...attempts(), job: job({ status: 'PrintDispatched', attemptCount: 4 }) }
  expect(parseCashHistoryPrintAttempts(current, 'branch', 'CASH-1', snapshot.snapshotId, job().printJobId, {}, snapshot).job.attemptCount).toBe(4)
  expect(samePrintJobIdentity(job(), current.job)).toBe(true)
  expect(samePrintJobIdentity(job(), job({ createdAt: '2026-11-27T03:00:00.0000000+00:00' }))).toBe(true)
  expect(samePrintJobIdentity(job(), job({ createdAt: '2026-11-27T03:00:00.0000001Z' }))).toBe(false)
  expect(samePrintJobIdentity(job(), job({ createdAt: '2026-11-27T04:00:00Z' }))).toBe(false)
})
it('rejects unsafe retry claims, invalid attempts, ambiguous continuation and reordered duplicate records', () => {
  const original = attempts(), attempt = original.items[0]
  for (const changed of [{ ...original, items: [{ ...attempt, automaticRetryAllowed: true }] }, { ...original, items: [{ ...attempt, finishedAt: null }] }, { ...original, items: [{ ...attempt, status: 'Failed', firstByteAt: time }] }, { ...original, items: [{ ...attempt, attemptNumber: 4 }] }, { ...original, items: [attempt, attempt] }, { ...original, items: [], nextCursor: 'cursor' }, { ...original, job: job({ printJobId: 'other' }) }]) expect(() => parseCashHistoryPrintAttempts(changed, 'branch', 'CASH-1', snapshot.snapshotId, job().printJobId, {}, snapshot)).toThrow()
})
it('preserves recorded array order without inferring attempt-number chronology', () => {
  const original = attempts(), first = original.items[0]
  const source = { ...original, items: [{ ...first, attemptNumber: 1, printAttemptId: 'earlier-row' }, first] }
  expect(parseCashHistoryPrintAttempts(source, 'branch', 'CASH-1', snapshot.snapshotId, job().printJobId, {}, snapshot).items.map(item => item.attemptNumber)).toEqual([1, 3])
})
it('shows uncertain status, recorded retry facts and source times with no hardware action', async () => {
  render(<CashHistoryPrintEvidence {...props} read={readEvidence()} attemptsRead={readAttempts()} />)
  await screen.findByText(/สถานะงานที่อ่านครั้งนี้/)
  expect(screen.getAllByText(/อาจมีกระดาษออกแล้ว/).length).toBeGreaterThan(0)
  expect(screen.getByText(/ค่าการส่งซ้ำอัตโนมัติที่บันทึก: ไม่อนุญาต/)).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^(พิมพ์|ส่งซ้ำ|ลองใหม่|จำลอง|เปิดลิ้นชัก)/ })).toBeNull()
  expect(screen.getByText(/ยังไม่ยืนยันว่ากระดาษออกจริง/)).toBeTruthy()
})
it('preserves independent missing-job and legacy attempt capability states', async () => {
  const read = readEvidence().mockResolvedValue(ok({ ...evidence(), job: null })), detail = readAttempts()
  const view = render(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={detail} />)
  await screen.findByText(/ไม่พบงานพิมพ์ต้นฉบับที่บันทึกไว้/)
  expect(detail).not.toHaveBeenCalled()
  view.rerender(<CashHistoryPrintEvidence {...props} read={readEvidence()} attemptsRead={undefined} />)
  await screen.findByText('ยังไม่เชื่อมต่อประวัติครั้งที่พยายามส่งจากบริการสาขารุ่นนี้')
  expect(screen.getByText(/งานต้นฉบับ branch:submit/)).toBeTruthy()
})
it('paginates attempts independently and accepts later current job observations', async () => {
  const first = { ...attempts(), nextCursor: 'cursor1' }, next = { ...attempts(), job: job({ status: 'PrintDispatched', attemptCount: 4 }), items: [{ ...attempts().items[0], printAttemptId: 'attempt2', attemptNumber: 2, status: 'Failed' as const, firstByteAt: null, automaticRetryAllowed: true }] }
  const read = readAttempts().mockResolvedValueOnce(ok(first)).mockResolvedValueOnce(ok(next))
  render(<CashHistoryPrintEvidence {...props} read={readEvidence()} attemptsRead={read} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านครั้งที่พยายามส่งเพิ่มเติม' }))
  await screen.findByText(/ครั้งที่ 2/)
  expect(screen.getByText(/ครั้งที่ 3/)).toBeTruthy()
  expect(read).toHaveBeenLastCalledWith('CASH-1', snapshot.snapshotId, job().printJobId, { limit: 20, cursor: 'cursor1' }, expect.any(AbortSignal))
  expect(screen.getByText(/สถานะงานที่อ่านครั้งนี้/).textContent).toContain('ส่งข้อมูลแล้ว')
})
it('releases pending pagination when its reader changes and ignores late old records', async () => {
  let finish!: (value: QueryResult<CashHistoryPrintAttemptsPage>) => void, signal: AbortSignal | undefined
  const read = readEvidence(), old = readAttempts().mockResolvedValueOnce(ok({ ...attempts(), nextCursor: 'old' })).mockImplementationOnce((_r, _s, _j, _q, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) })
  const view = render(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={old} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านครั้งที่พยายามส่งเพิ่มเติม' }))
  const next = readAttempts().mockResolvedValueOnce(ok({ ...attempts(), nextCursor: 'next' })).mockResolvedValueOnce(ok({ ...attempts(), items: [] }))
  view.rerender(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={next} />)
  const more = await screen.findByRole('button', { name: 'อ่านครั้งที่พยายามส่งเพิ่มเติม' })
  expect(signal?.aborted).toBe(true); expect((more as HTMLButtonElement).disabled).toBe(false)
  fireEvent.click(more); await waitFor(() => expect(next).toHaveBeenCalledTimes(2))
  await act(async () => { finish(ok({ ...attempts(), items: [{ ...attempts().items[0], attemptNumber: 1 }] })); await Promise.resolve() })
  expect(screen.queryByText(/ครั้งที่ 1/)).toBeNull()
})
it('refresh cancels pending source reads and clears stale attempt selection', async () => {
  let signal: AbortSignal | undefined, finish!: (value: QueryResult<Evidence>) => void
  const read = readEvidence().mockImplementationOnce((_r, _s, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) }).mockResolvedValue(ok({ ...evidence(), job: null }))
  render(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={readAttempts()} />)
  fireEvent.click(screen.getByRole('button', { name: 'อ่านหลักฐานงานพิมพ์ใหม่' }))
  await screen.findByText(/ไม่พบงานพิมพ์ต้นฉบับ/); expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(evidence())); await Promise.resolve() })
  expect(screen.queryByText(/งานต้นฉบับ branch:submit/)).toBeNull()
})
it('hides old evidence when metadata changes under the same snapshot identity', async () => {
  let signal: AbortSignal | undefined
  const read = readEvidence().mockResolvedValueOnce(ok(evidence())).mockImplementationOnce((_r, _s, abort) => { signal = abort; return new Promise(() => {}) })
  const view = render(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={readAttempts()} />)
  await screen.findByText(/สถานะงานที่อ่านครั้งนี้/)
  view.rerender(<CashHistoryPrintEvidence {...props} snapshot={{ ...snapshot, productionStationId: 'other-station' }} read={read} attemptsRead={readAttempts()} />)
  await screen.findByText('กำลังอ่านหลักฐานงานพิมพ์')
  expect(screen.queryByText(/งานต้นฉบับ branch:submit/)).toBeNull()
  expect(screen.queryByText(/สถานะงานที่อ่านครั้งนี้/)).toBeNull()
  view.unmount(); expect(signal?.aborted).toBe(true)
})
it('aborts subject/branch and lock-unmount requests without accepting stale evidence', async () => {
  const signals: AbortSignal[] = [], read = readEvidence().mockImplementation((_r, _s, signal) => { signals.push(signal!); return new Promise(() => {}) })
  const view = render(<CashHistoryPrintEvidence {...props} read={read} attemptsRead={readAttempts()} />)
  view.rerender(<CashHistoryPrintEvidence {...props} branchId="other" read={read} attemptsRead={readAttempts()} />)
  expect(signals[0].aborted).toBe(true)
  view.unmount(); expect(signals[1].aborted).toBe(true)
})
it('focuses original production from a captured bill line and keeps financial facts on print failure', async () => {
  const captured = cashItemizedDetail(), productionRead = vi.fn(async () => ok({ ...subject, items: [snapshot], nextCursor: null })), detailRead = vi.fn(async () => ok({ ...subject, snapshot, lines: [{ orderLineId: 'LINE-1', lineSequence: 1, displayName: 'Original item', quantity: 2, fulfillmentType: 'Takeaway' as const, comment: null, options: [] }] }))
  render(<CashHistoryWorkspace scopeKey="branch" port={{ cashHistory: vi.fn(async () => ok(cashPage([captured.transaction]))), cashHistoryDetail: vi.fn(async () => ok(captured)), cashHistoryProductionOrders: productionRead, cashHistoryProductionOrderDetail: detailRead, cashHistoryPrintEvidence: readEvidence().mockResolvedValue({ kind: 'unavailable', reason: 'http' }), cashHistoryPrintAttempts: readAttempts() }} />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  const bill = within(await screen.findByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' }))
  fireEvent.click(bill.getByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้' }))
  await waitFor(() => expect(productionRead).toHaveBeenLastCalledWith('CASH-1', { limit: 20, orderLineId: 'LINE-1' }, expect.any(AbortSignal)))
  fireEvent.click(await screen.findByRole('button', { name: /ดูงานผลิตเดิม kitchen/ }))
  await screen.findByText('ยังอ่านหลักฐานงานพิมพ์ไม่ได้ ข้อมูลรายการและยอดชำระยังคงเดิม')
  expect(screen.getByText(/ชำระเงินสดสำเร็จ/)).toBeTruthy(); expect(screen.getByText('Original item')).toBeTruthy()
})
it('does not offer original-line links on older clients', async () => {
  const captured = cashItemizedDetail()
  render(<CashHistoryWorkspace scopeKey="legacy" port={{ cashHistory: vi.fn(async () => ok(cashPage([captured.transaction]))), cashHistoryDetail: vi.fn(async () => ok(captured)) }} />)
  fireEvent.click(await screen.findByRole('button', { name: /TEST-001/ }))
  await screen.findByRole('region', { name: 'รายการสินค้าจากใบคิดเงินเดิม' })
  expect(screen.queryByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้' })).toBeNull()
})
it('uses authenticated no-store GETs and rejects invalid query and identities before transport', async () => {
  const { createOrderingClient } = await import('../api/orderingClient.ts'), requests: Array<{ url: string; init: RequestInit }> = []
  const session = { serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'test-device', employeeId: 'test-employee', deviceCredential: 'v1.test', employeeSessionToken: 'v1.test' }
  const client = createOrderingClient(session, { fetch: async (url, init) => { requests.push({ url: String(url), init }); return new Response(JSON.stringify(requests.length === 1 ? evidence() : attempts()), { status: 200, headers: { 'content-type': 'application/json' } }) } })
  expect((await client.cashHistoryPrintEvidence('CASH-1', snapshot.snapshotId)).kind).toBe('ok')
  expect((await client.cashHistoryPrintAttempts('CASH-1', snapshot.snapshotId, job().printJobId, { cursor: 'valid' })).kind).toBe('ok')
  expect(requests[0].url).toContain(encodeURIComponent(snapshot.snapshotId) + '/print-evidence')
  expect(requests[1].url).toContain(encodeURIComponent(job().printJobId) + '/attempts?limit=20&cursor=valid')
  for (const request of requests) { expect(request.init.method).toBe('GET'); expect(request.init.body).toBeUndefined(); expect(request.init.cache).toBe('no-store'); expect(new Headers(request.init.headers).get('Authorization')).toBe('Device v1.test'); expect(new Headers(request.init.headers).get('X-POS-Employee-Session')).toBe('v1.test') }
  expect(await client.cashHistoryPrintEvidence(' bad', snapshot.snapshotId)).toEqual({ kind: 'unavailable', reason: 'protocol' })
  expect(await client.cashHistoryPrintAttempts('CASH-1', snapshot.snapshotId, 'bad\u0085id')).toEqual({ kind: 'unavailable', reason: 'protocol' })
  expect(await client.cashHistoryPrintAttempts('CASH-1', snapshot.snapshotId, job().printJobId, { limit: 51 })).toEqual({ kind: 'unavailable', reason: 'protocol' })
  expect(requests).toHaveLength(2)
})
