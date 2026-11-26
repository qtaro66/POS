import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import { CashHistoryContextPanels } from './CashHistoryContextPanels.tsx'
import { CashHistoryWorkspace } from './CashHistoryWorkspace.tsx'
import { parseCashHistoryShiftContext, parseCashHistoryReceiptEvidence, type CashHistoryShiftContext, type CashHistoryReceiptEvidence } from '../api/cashHistoryContextContracts.ts'
import { cashItem, cashPage, cashDetail } from '../api/cashHistoryTestFixtures.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const item = cashItem()
const ok = <T,>(value: T): QueryResult<T> => ({ kind: 'ok', value })
const subject = { schemaVersion: 1 as const, branchId: 'branch', cashRequestId: item.requestId, tableSessionId: item.tableSessionId, asOf: item.capturedAt }
const shift = (): CashHistoryShiftContext => ({ ...subject, storeSessionId: item.storeSessionId, shiftId: item.shiftId, businessDate: item.businessDate, entries: [{ requestId: 'close-1', action: 'CloseShift', occurredAt: item.capturedAt, branchLocalDate: item.businessDate, employeeId: 'EMP-ORIGINAL', deviceId: 'DEVICE-ORIGINAL', cashAmount: null, closing: { closesStore: false, expectedCashAmount: 100, actualCashAmount: null, cashDifference: null } }] })
const receipt = (): CashHistoryReceiptEvidence => ({ ...subject, receiptNumber: item.receiptNumber, queuedAt: item.capturedAt, capturedPrintStatus: 'QueuedForSimulation', simulation: null })
const shiftReader = () => vi.fn(async (_id: string, _signal?: AbortSignal): Promise<QueryResult<CashHistoryShiftContext>> => ok(shift()))
const receiptReader = () => vi.fn(async (_id: string, _signal?: AbortSignal): Promise<QueryResult<CashHistoryReceiptEvidence>> => ok(receipt()))
const panels = { branchId: 'branch', item }

it('preserves unreconciled and counted closings and checks captured subject identities', () => {
  expect(parseCashHistoryShiftContext(shift(), 'branch', item.requestId, item).entries[0].closing?.actualCashAmount).toBeNull()
  const counted = shift(); counted.entries[0].cashAmount = 90; counted.entries[0].closing = { closesStore: false, expectedCashAmount: 100, actualCashAmount: 90, cashDifference: -10 }
  expect(parseCashHistoryShiftContext(counted, 'branch', item.requestId, item).entries[0].closing?.cashDifference).toBe(-10)
  const large = shift(); large.entries[0].cashAmount = 10000000000.02; large.entries[0].closing = { closesStore: false, expectedCashAmount: 10000000000.01, actualCashAmount: 10000000000.02, cashDifference: 0.01 }
  expect(parseCashHistoryShiftContext(large, 'branch', item.requestId).entries[0].closing?.cashDifference).toBe(0.01)
  expect(() => parseCashHistoryShiftContext({ ...large, entries: [{ ...large.entries[0], closing: { ...large.entries[0].closing, cashDifference: 0.1 } }] }, 'branch', item.requestId)).toThrow()
  for (const changed of [{ ...shift(), branchId: 'other' }, { ...shift(), tableSessionId: 'OTHER' }, { ...shift(), shiftId: 'OTHER' }, { ...shift(), storeSessionId: 'OTHER' }, { ...shift(), businessDate: '2026-01-01' }]) expect(() => parseCashHistoryShiftContext(changed, 'branch', item.requestId, item)).toThrow()
  expect(() => parseCashHistoryShiftContext({ ...shift(), entries: [{ ...shift().entries[0], cashAmount: 0 }] }, 'branch', item.requestId)).toThrow()
  expect(() => parseCashHistoryShiftContext({ ...shift(), entries: [{ ...shift().entries[0], action: 'CloseStore', closing: { ...shift().entries[0].closing, closesStore: true } }] }, 'branch', item.requestId)).toThrow()
  expect(() => parseCashHistoryShiftContext({ ...shift(), entries: [{ ...counted.entries[0], closing: { ...counted.entries[0].closing, cashDifference: 0 } }] }, 'branch', item.requestId)).toThrow()
})
it('rejects ambiguous lifecycle ordering, undefined actions, duplicate requests and invented opening closing data', () => {
  const opening = { ...shift().entries[0], requestId: 'open-1', action: 'OpenShift' as const, closing: null }
  expect(parseCashHistoryShiftContext({ ...shift(), entries: [opening, shift().entries[0]] }, 'branch', item.requestId).entries).toHaveLength(2)
  expect(parseCashHistoryShiftContext({ ...shift(), entries: [] }, 'branch', item.requestId).entries).toEqual([])
  for (const entries of [[shift().entries[0], opening], [opening, opening], [opening, opening, opening], [{ ...opening, action: 'Unknown' }], [{ ...opening, action: 'OpenStore' }], [{ ...opening, closing: shift().entries[0].closing }]]) expect(() => parseCashHistoryShiftContext({ ...shift(), entries }, 'branch', item.requestId)).toThrow()
  expect(() => parseCashHistoryShiftContext({ ...shift(), entries: [{ ...opening, action: 'OpenStore', cashAmount: 100, branchLocalDate: '2026-01-01' }] }, 'branch', item.requestId)).toThrow()
})
it('distinguishes missing receipt results, recorded failure and bounded safe original text', () => {
  expect(parseCashHistoryReceiptEvidence(receipt(), 'branch', item.requestId, item).simulation).toBeNull()
  expect(parseCashHistoryReceiptEvidence({ ...receipt(), simulation: { status: 'Failed', text: null } }, 'branch', item.requestId).simulation?.status).toBe('Failed')
  const text = '<script>original</script>\r\nตัวเลือก\tเดิม'
  expect(parseCashHistoryReceiptEvidence({ ...receipt(), simulation: { status: 'Simulated', text } }, 'branch', item.requestId).simulation?.text).toBe(text)
  for (const simulation of [undefined, { status: 'Failed', text: 'invented' }, { status: 'Simulated', text: null }, { status: 'Simulated', text: ' ' }, { status: 'Completed', text: null }, { status: 'Simulated', text: 'bad\u0085' }, { status: 'Simulated', text: 'ก'.repeat(45000) }]) expect(() => parseCashHistoryReceiptEvidence({ ...receipt(), simulation }, 'branch', item.requestId)).toThrow()
  for (const changed of [{ receiptNumber: 'OTHER' }, { queuedAt: '2026-11-26T23:00:00Z' }, { tableSessionId: 'OTHER' }, { capturedPrintStatus: 'Completed' }]) expect(() => parseCashHistoryReceiptEvidence({ ...receipt(), ...changed }, 'branch', item.requestId, item)).toThrow()
  expect(parseCashHistoryReceiptEvidence({ ...receipt(), queuedAt: item.capturedAt.slice(0, 19) + '.0000000+00:00' }, 'branch', item.requestId, item).queuedAt).toContain('.0000000')
  expect(() => parseCashHistoryReceiptEvidence({ ...receipt(), queuedAt: item.capturedAt.slice(0, 19) + '.0000001Z' }, 'branch', item.requestId, item)).toThrow()
})
it('shows captured shift evidence independently from receipt results without inferring a current shift', async () => {
  const view = render(<CashHistoryContextPanels {...panels} shiftRead={shiftReader()} receiptRead={receiptReader()} />)
  await screen.findByText('ไม่ได้บันทึกยอดนับเงินสด จึงยังไม่มียอดต่างจากการกระทบยอด')
  await screen.findByText('ยังไม่มีผลจำลองใบเสร็จที่บันทึกไว้')
  expect(screen.getByText(/EMP-ORIGINAL/).textContent).toContain('DEVICE-ORIGINAL')
  expect(screen.getByText(/ไม่ใช่สถานะกะปัจจุบัน/)).toBeTruthy()
  const counted = shift(); counted.entries[0].cashAmount = 90; counted.entries[0].closing = { closesStore: false, expectedCashAmount: 100, actualCashAmount: 90, cashDifference: -10 }
  view.rerender(<CashHistoryContextPanels {...panels} shiftRead={shiftReader().mockResolvedValue(ok(counted))} receiptRead={receiptReader()} />)
  await screen.findByText('เงินสดที่นับได้ 90.00 บาท · ยอดต่างที่บันทึก -10.00 บาท')
  expect(screen.queryByText(/ไม่ได้บันทึกยอดนับเงินสด/)).toBeNull()
})
it('renders saved receipt text as plain text and offers no printing, simulation or cash commands', async () => {
  const saved = receipt(); saved.simulation = { status: 'Simulated', text: '<script>original</script>\nบรรทัดเดิม' }
  render(<CashHistoryContextPanels {...panels} shiftRead={shiftReader()} receiptRead={receiptReader().mockResolvedValue(ok(saved))} />)
  const text = await screen.findByLabelText('ข้อความใบเสร็จจำลองเดิม')
  expect(text.textContent).toBe(saved.simulation.text)
  expect(document.querySelector('script')).toBeNull()
  expect(screen.getByText(/ไม่ใช่หลักฐานว่าพิมพ์จริงสำเร็จ/)).toBeTruthy()
  expect(screen.queryByRole('button', { name: /^(พิมพ์|จำลอง|ยกเลิก|คืนเงิน|เปิดลิ้นชัก)/ })).toBeNull()
})
it('aborts old reader requests and prevents stale shift evidence from reappearing', async () => {
  let finish!: (value: QueryResult<CashHistoryShiftContext>) => void, signal: AbortSignal | undefined
  const old = shiftReader().mockImplementationOnce((_id, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) })
  const view = render(<CashHistoryContextPanels {...panels} shiftRead={old} receiptRead={receiptReader()} />)
  const empty = { ...shift(), entries: [] }
  view.rerender(<CashHistoryContextPanels {...panels} shiftRead={shiftReader().mockResolvedValue(ok(empty))} receiptRead={receiptReader()} />)
  await screen.findByText(/ไม่พบหลักฐานรอบร้านและกะที่บันทึกไว้/)
  expect(signal?.aborted).toBe(true)
  await act(async () => { finish(ok(shift())); await Promise.resolve() })
  expect(screen.queryByText(/EMP-ORIGINAL/)).toBeNull()
})
it('refresh cancels pending receipt evidence and ignores its late saved result', async () => {
  let finish!: (value: QueryResult<CashHistoryReceiptEvidence>) => void, signal: AbortSignal | undefined
  const read = receiptReader().mockImplementationOnce((_id, abort) => { signal = abort; return new Promise(resolve => { finish = resolve }) })
  render(<CashHistoryContextPanels {...panels} shiftRead={undefined} receiptRead={read} />)
  fireEvent.click(screen.getByRole('button', { name: 'อ่านหลักฐานใบเสร็จใหม่' }))
  await screen.findByText('ยังไม่มีผลจำลองใบเสร็จที่บันทึกไว้')
  expect(signal?.aborted).toBe(true)
  const late = receipt(); late.simulation = { status: 'Simulated', text: 'OBSOLETE-RECEIPT' }
  await act(async () => { finish(ok(late)); await Promise.resolve() })
  expect(screen.queryByText('OBSOLETE-RECEIPT')).toBeNull()
})
it('scope changes abort both readers and reject evidence for the previous branch', async () => {
  let shiftSignal: AbortSignal | undefined, receiptSignal: AbortSignal | undefined
  const readShift = shiftReader().mockImplementationOnce((_id, signal) => { shiftSignal = signal; return new Promise(() => {}) })
  const readReceipt = receiptReader().mockImplementationOnce((_id, signal) => { receiptSignal = signal; return new Promise(() => {}) })
  const view = render(<CashHistoryContextPanels {...panels} shiftRead={readShift} receiptRead={readReceipt} />)
  view.rerender(<CashHistoryContextPanels {...panels} branchId="OTHER" shiftRead={readShift} receiptRead={readReceipt} />)
  await screen.findByText('ยังอ่านหลักฐานรอบร้านและกะไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  await screen.findByText('ยังอ่านหลักฐานใบเสร็จไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  expect(shiftSignal?.aborted).toBe(true); expect(receiptSignal?.aborted).toBe(true)
})
it('unmounting for lock aborts both pending reads', () => {
  const signals: AbortSignal[] = []
  const readShift = shiftReader().mockImplementation((_id, signal) => { signals.push(signal!); return new Promise(() => {}) })
  const readReceipt = receiptReader().mockImplementation((_id, signal) => { signals.push(signal!); return new Promise(() => {}) })
  const view = render(<CashHistoryContextPanels {...panels} shiftRead={readShift} receiptRead={readReceipt} />)
  view.unmount()
  expect(signals).toHaveLength(2); expect(signals.every(signal => signal.aborted)).toBe(true)
})
it('keeps financial detail on independent failures and explicitly marks legacy capabilities', async () => {
  const port = { cashHistory: vi.fn(async () => ok(cashPage())), cashHistoryDetail: vi.fn(async () => ok(cashDetail())) }
  const view = render(<CashHistoryWorkspace scopeKey="one" port={port} />)
  fireEvent.click(await screen.findByRole('button', { name: new RegExp(item.receiptNumber) }))
  await screen.findByText('ยังไม่เชื่อมต่อหลักฐานรอบร้านและกะจากบริการสาขารุ่นนี้')
  await screen.findByText('ยังไม่เชื่อมต่อหลักฐานใบเสร็จจากบริการสาขารุ่นนี้')
  view.rerender(<CashHistoryWorkspace scopeKey="one" port={{ ...port, cashHistoryShiftContext: shiftReader().mockResolvedValue({ kind: 'unavailable', reason: 'http' }), cashHistoryReceiptEvidence: receiptReader().mockResolvedValue({ kind: 'unavailable', reason: 'http' }) }} />)
  await screen.findByText('ยังอ่านหลักฐานรอบร้านและกะไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  await screen.findByText('ยังอ่านหลักฐานใบเสร็จไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  expect(screen.getByText(/ชำระเงินสดสำเร็จ/)).toBeTruthy()
  expect(within(screen.getByRole('region', { name: 'รายละเอียดธุรกรรมเงินสด' })).getByText('ยอดชำระที่มีผล')).toBeTruthy()
})
it('uses authenticated cache-free GETs with encoded IDs and rejects invalid inputs before transport', async () => {
  const { createOrderingClient } = await import('../api/orderingClient.ts')
  const requests: Array<{ url: string; init: RequestInit }> = [], id = 'CASH /:1'
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'test-device', employeeId: 'test-employee', deviceCredential: 'v1.test', employeeSessionToken: 'v1.test' }, { fetch: async (url, init) => { requests.push({ url: String(url), init }); return new Response(JSON.stringify({ ...(requests.length === 1 ? shift() : receipt()), cashRequestId: id }), { status: 200, headers: { 'content-type': 'application/json' } }) } })
  expect((await client.cashHistoryShiftContext(id)).kind).toBe('ok'); expect((await client.cashHistoryReceiptEvidence(id)).kind).toBe('ok')
  expect(requests[0].url).toContain('/cash-sales/' + encodeURIComponent(id) + '/shift-context')
  expect(requests[1].url).toContain('/cash-sales/' + encodeURIComponent(id) + '/receipt-evidence')
  for (const request of requests) { expect(request.init.method).toBe('GET'); expect(request.init.body).toBeUndefined(); expect(request.init.cache).toBe('no-store'); expect(new Headers(request.init.headers).get('Authorization')).toBe('Device v1.test'); expect(new Headers(request.init.headers).get('X-POS-Employee-Session')).toBe('v1.test') }
  expect(await client.cashHistoryShiftContext(' bad')).toEqual({ kind: 'unavailable', reason: 'protocol' }); expect(await client.cashHistoryReceiptEvidence('bad\u0085')).toEqual({ kind: 'unavailable', reason: 'protocol' }); expect(requests).toHaveLength(2)
})
it('rejects mismatched evidence before display and represents client HTTP failures distinctly', async () => {
  render(<CashHistoryContextPanels {...panels} shiftRead={shiftReader().mockResolvedValue(ok({ ...shift(), shiftId: 'WRONG' }))} receiptRead={receiptReader().mockResolvedValue(ok({ ...receipt(), receiptNumber: 'WRONG' }))} />)
  await screen.findByText('ยังอ่านหลักฐานรอบร้านและกะไม่ได้ ยอดชำระที่บันทึกยังคงเดิม'); await screen.findByText('ยังอ่านหลักฐานใบเสร็จไม่ได้ ยอดชำระที่บันทึกยังคงเดิม')
  const { createOrderingClient } = await import('../api/orderingClient.ts')
  const responses = [new Response('{}', { status: 404, headers: { 'content-type': 'application/json' } }), new Response('{}', { status: 403, headers: { 'content-type': 'application/json' } }), new Response(JSON.stringify({ ...shift(), branchId: 'other' }), { status: 200, headers: { 'content-type': 'application/json' } })]
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'test-device', employeeId: 'test-employee', deviceCredential: 'v1.test', employeeSessionToken: 'v1.test' }, { fetch: async () => responses.shift()! })
  expect(await client.cashHistoryReceiptEvidence(item.requestId)).toEqual({ kind: 'notFound' })
  expect(await client.cashHistoryShiftContext(item.requestId)).toEqual({ kind: 'unavailable', reason: 'http', httpStatus: 403 })
  expect(await client.cashHistoryShiftContext(item.requestId)).toEqual({ kind: 'unavailable', reason: 'protocol' })
})
