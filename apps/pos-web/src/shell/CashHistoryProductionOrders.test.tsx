import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryProductionOrders } from './CashHistoryProductionOrders.tsx'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import { parseCashHistoryProductionDetail, parseCashHistoryProductionPage } from '../api/cashHistoryProductionContracts.ts'
import type { CashHistoryProductionQuery, CashHistoryProductionPage, CashHistoryProductionDetail } from '../api/cashHistoryProductionContracts.ts'
import type { QueryResult } from '../api/orderingClient.ts'
import { cashDetail, cashItem, cashPage } from '../api/cashHistoryTestFixtures.ts'
import { cancellationEvidence, eventPage } from '../api/cashHistoryEventTestFixtures.ts'
const props = { branchId: 'branch', requestId: 'CASH-1', tableSessionId: 'SESSION-1' }
const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const snapshot = { snapshotId: 'branch:submit:production:1', orderBatchId: 'BATCH-1', submittedRequestId: 'SUBMIT-1', tableSessionId: 'SESSION-1', tableId: 'A01', productionStationId: 'kitchen', submittedAt: '2026-11-26T10:00:00Z', lineCount: 1, orderLineIds: ['LINE-1'] }
const page = (): CashHistoryProductionPage => ({ schemaVersion: 1, ...props, cashRequestId: props.requestId, asOf: '2026-11-26T10:00:00Z', items: [snapshot], nextCursor: null })
const detail = (): CashHistoryProductionDetail => ({ schemaVersion: 1, ...props, cashRequestId: props.requestId, asOf: '2026-11-26T10:00:00Z', snapshot: { ...snapshot, orderLineIds: [...snapshot.orderLineIds] }, lines: [{ orderLineId: 'LINE-1', lineSequence: 2, displayName: 'ชื่อเดิม\nสอง', quantity: 4, fulfillmentType: 'Takeaway', comment: '<script>บันทึกเดิม</script>\nสอง', options: [{ optionGroupId: null, displayName: 'ตัวเลือกเดิม', quantityPerItem: 2 }] }] })
const reader = () => vi.fn(async (_id: string, _query: CashHistoryProductionQuery = {}, _signal?: AbortSignal): Promise<QueryResult<CashHistoryProductionPage>> => ok(page()))
const detailReader = () => vi.fn(async (_id: string, _snapshot: string, _signal?: AbortSignal): Promise<QueryResult<CashHistoryProductionDetail>> => ok(detail()))
it('checks subject, filtered membership, ordered metadata and original detail without filling current names', () => {
  expect(parseCashHistoryProductionDetail(detail(), 'branch', 'CASH-1', snapshot.snapshotId).lines[0].quantity).toBe(4)
  expect(() => parseCashHistoryProductionPage(page(), 'other', 'CASH-1', {})).toThrow()
  expect(() => parseCashHistoryProductionPage(page(), 'branch', 'CASH-1', { orderLineId: 'OTHER' })).toThrow()
  expect(() => parseCashHistoryProductionDetail({ ...detail(), snapshot: { ...snapshot, orderLineIds: ['OTHER'] } }, 'branch', 'CASH-1', snapshot.snapshotId)).toThrow()
  expect(() => parseCashHistoryProductionDetail({ ...detail(), lines: [{ ...detail().lines[0], fulfillmentType: 'Unknown' }] }, 'branch', 'CASH-1', snapshot.snapshotId)).toThrow()
  expect(() => parseCashHistoryProductionDetail({ ...detail(), lines: [{ ...detail().lines[0], displayName: '\u0085' }] }, 'branch', 'CASH-1', snapshot.snapshotId)).toThrow()
})
it('reads whole original snapshot and applies raw exact filters', async () => {
  const read = reader(), detailRead = detailReader()
  render(<CashHistoryProductionOrders {...props} read={read} detailRead={detailRead} />)
  fireEvent.click(await screen.findByRole('button', { name: /ดูงานผลิตเดิม kitchen/ }))
  expect(await screen.findByText('จำนวนตอนส่ง 4 · กลับบ้าน')).toBeTruthy()
  expect(screen.getByText(/หมายเหตุเดิม/).textContent).toContain('<script>บันทึกเดิม</script>\nสอง')
  expect(document.querySelector('script')).toBeNull()
  fireEvent.change(screen.getByLabelText('ชุดออเดอร์ตอนส่ง'), { target: { value: 'BATCH-1' } })
  fireEvent.change(screen.getByLabelText('รหัสรายการตอนส่ง'), { target: { value: 'LINE-1' } })
  fireEvent.change(screen.getByLabelText('สถานีผลิตเดิม'), { target: { value: 'kitchen' } })
  fireEvent.click(screen.getByRole('button', { name: 'ค้นหางานผลิตเดิม' }))
  await waitFor(() => expect(read).toHaveBeenLastCalledWith('CASH-1', { limit: 20, orderBatchId: 'BATCH-1', orderLineId: 'LINE-1', productionStationId: 'kitchen' }, expect.any(AbortSignal)))
  expect(screen.queryByRole('button', { name: /^พิมพ์|^ส่งใหม่/ })).toBeNull()
})
it('cancels late detail and discards selection after its list reader changes', async () => {
  let finish!: (v: QueryResult<CashHistoryProductionDetail>) => void, signal: AbortSignal | undefined
  const oldRead = reader(), readDetail = detailReader().mockImplementationOnce((_id, _snapshot, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) })
  const view = render(<CashHistoryProductionOrders {...props} read={oldRead} detailRead={readDetail} />)
  fireEvent.click(await screen.findByRole('button', { name: /ดูงานผลิตเดิม kitchen/ }))
  await screen.findByText('กำลังอ่านข้อมูลตอนส่ง')
  view.rerender(<CashHistoryProductionOrders {...props} read={reader()} detailRead={readDetail} />)
  await screen.findByRole('button', { name: /ดูงานผลิตเดิม kitchen/ })
  expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(detail())); await Promise.resolve() })
  expect(screen.queryByText('จำนวนตอนส่ง 4 · กลับบ้าน')).toBeNull()
})
it('resets pending page and filters when cancellation focus changes', async () => {
  let signal: AbortSignal | undefined
  const read = reader().mockImplementationOnce((_id, _query, abort) => { signal = abort; return new Promise(() => {}) })
  const view = render(<CashHistoryProductionOrders {...props} read={read} detailRead={detailReader()} />)
  view.rerender(<CashHistoryProductionOrders {...props} read={read} detailRead={detailReader()} focus={{ orderLineId: 'LINE-1', revision: 1 }} />)
  await screen.findByRole('button', { name: /ดูงานผลิตเดิม kitchen/ })
  expect(signal?.aborted).toBe(true)
  expect((screen.getByLabelText('รหัสรายการตอนส่ง') as HTMLInputElement).value).toBe('LINE-1')
  expect(document.activeElement?.textContent).toBe('งานผลิตที่บันทึกตอนส่ง')
})
it('retains financial detail when source is unavailable and does not offer links on legacy readers', async () => {
  render(<CashHistoryWorkspace scopeKey="x" port={{ cashHistory: vi.fn(async () => ok(cashPage())), cashHistoryDetail: vi.fn(async () => ok(cashDetail())), cashHistoryEvents: vi.fn(async (_id, q) => ok(eventPage(q?.source, [cancellationEvidence()]))) }} />)
  fireEvent.click(await screen.findByRole('button', { name: new RegExp(cashItem().receiptNumber) }))
  await screen.findByText('ยังไม่เชื่อมต่อข้อมูลงานผลิตเดิมจากบริการสาขารุ่นนี้')
  expect(screen.getByText(/ชำระเงินสดสำเร็จ/)).toBeTruthy()
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  await screen.findByText('ยืนยันยกเลิกบางส่วน')
  expect(screen.queryByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้' })).toBeNull()
})
it('passes cancellation inspection to an exact original line filter while preserving money on failure', async () => {
  const read = reader().mockResolvedValueOnce(ok(page())).mockResolvedValue({ kind: 'unavailable', reason: 'http' })
  render(<CashHistoryWorkspace scopeKey="x" port={{ cashHistory: vi.fn(async () => ok(cashPage())), cashHistoryDetail: vi.fn(async () => ok(cashDetail())), cashHistoryEvents: vi.fn(async (_id, q) => ok(eventPage(q?.source))), cashHistoryProductionOrders: read, cashHistoryProductionOrderDetail: detailReader() }} />)
  fireEvent.click(await screen.findByRole('button', { name: new RegExp(cashItem().receiptNumber) }))
  await screen.findByText(/ชำระเงินสดสำเร็จ/)
  fireEvent.click(screen.getByRole('button', { name: 'หลักฐานยืนยันการยกเลิก' }))
  fireEvent.click(await screen.findByRole('button', { name: 'ดูข้อมูลตอนส่งรายการนี้' }))
  await waitFor(() => expect(read).toHaveBeenLastCalledWith('CASH-1', { limit: 20, orderLineId: 'CANCELLED-LINE' }, expect.any(AbortSignal)))
  await screen.findByText('ยังอ่านงานผลิตเดิมไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  expect(screen.getByText(/ชำระเงินสดสำเร็จ/)).toBeTruthy()
})
it('aborts old pagination and releases its loading state on reader replacement', async () => {
  let signal: AbortSignal | undefined, finish!: (v: QueryResult<CashHistoryProductionPage>) => void
  const old = reader().mockResolvedValueOnce(ok({ ...page(), nextCursor: 'cursor1' })).mockImplementationOnce((_id, _q, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) })
  const view = render(<CashHistoryProductionOrders {...props} read={old} detailRead={detailReader()} />)
  fireEvent.click(await screen.findByRole('button', { name: 'อ่านงานผลิตเดิมเพิ่มเติม' }))
  const next = reader().mockResolvedValueOnce(ok({ ...page(), nextCursor: 'cursor2' })).mockResolvedValue(ok({ ...page(), items: [] }))
  view.rerender(<CashHistoryProductionOrders {...props} read={next} detailRead={detailReader()} />)
  const more = await screen.findByRole('button', { name: 'อ่านงานผลิตเดิมเพิ่มเติม' })
  expect(signal?.aborted).toBe(true)
  expect((more as HTMLButtonElement).disabled).toBe(false)
  fireEvent.click(more)
  await waitFor(() => expect(next).toHaveBeenCalledTimes(2))
  await act(async () => { finish(ok({ ...page(), items: [{ ...snapshot, snapshotId: 'OBSOLETE', productionStationId: 'BOGUS' }] })); await Promise.resolve() })
  expect(screen.queryByRole('button', { name: /ดูงานผลิตเดิม BOGUS/ })).toBeNull()
  expect(screen.getAllByRole('button', { name: /ดูงานผลิตเดิม kitchen/ })).toHaveLength(1)
})
it('uses authenticated cache-free GETs, encodes composite snapshot identities, and rejects bad filters before transport', async () => {
  const { createOrderingClient } = await import('../api/orderingClient.ts')
  const requests: Array<{ url: string; init: RequestInit }> = []
  const session = { serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'test-device', employeeId: 'test-employee', deviceCredential: 'v1.test', employeeSessionToken: 'v1.test' }
  const client = createOrderingClient(session, { fetch: async (url, init) => { requests.push({ url: String(url), init }); return new Response(JSON.stringify(requests.length === 1 ? page() : detail()), { status: 200, headers: { 'content-type': 'application/json' } }) } })
  expect((await client.cashHistoryProductionOrders('CASH-1', { orderLineId: 'LINE-1' })).kind).toBe('ok')
  expect((await client.cashHistoryProductionOrderDetail('CASH-1', snapshot.snapshotId)).kind).toBe('ok')
  expect(requests[0].url).toContain('/cash-sales/CASH-1/production-orders?limit=20&orderLineId=LINE-1')
  expect(requests[1].url).toContain(encodeURIComponent(snapshot.snapshotId))
  for (const request of requests) { expect(request.init.method).toBe('GET'); expect(request.init.body).toBeUndefined(); expect(request.init.cache).toBe('no-store'); expect(new Headers(request.init.headers).get('Authorization')).toBe('Device v1.test'); expect(new Headers(request.init.headers).get('X-POS-Employee-Session')).toBe('v1.test') }
  expect(await client.cashHistoryProductionOrders('CASH-1', { orderLineId: ' LINE-1' })).toEqual({ kind: 'unavailable', reason: 'protocol' })
  expect(await client.cashHistoryProductionOrderDetail('CASH-1', 'bad\u0085id')).toEqual({ kind: 'unavailable', reason: 'protocol' })
  expect(requests).toHaveLength(2)
})
