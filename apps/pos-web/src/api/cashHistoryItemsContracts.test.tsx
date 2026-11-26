import { expect, it, vi } from 'vitest'
import { createOrderingClient } from './orderingClient.ts'
import { parseCashHistoryDetail, parseCashHistoryPage, validateCashHistoryQuery, type CashHistoryDetail } from './cashHistoryContracts.ts'
import { cashDetail, cashItem, cashItemizedDetail, cashPage } from './cashHistoryTestFixtures.ts'

it('binds original table and order batch filters to authenticated URLs and every continuation', async () => {
  const detail = cashItemizedDetail(), urls: URL[] = []
  const fetcher = vi.fn(async (url: string | URL | Request) => {
    urls.push(new URL(String(url)))
    return new Response(JSON.stringify(cashPage([detail.transaction])), { status: 200, headers: { 'Content-Type': 'application/json' } })
  })
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example', branchId: 'branch', employeeId: 'employee', deviceId: 'device', deviceCredential: 'credential', employeeSessionToken: 'token' }, { fetch: fetcher as typeof fetch })
  expect((await client.cashHistory({ tableId: 'A01', orderBatchId: 'ORDER-1' })).kind).toBe('ok')
  expect((await client.cashHistory({ tableId: 'A01', orderBatchId: 'ORDER-1', cursor: 'cursor2' })).kind).toBe('ok')
  expect(urls[1].searchParams.get('tableId')).toBe('A01'); expect(urls[1].searchParams.get('orderBatchId')).toBe('ORDER-1')
  expect(urls[1].searchParams.get('cursor')).toBe('cursor2')
  const count = fetcher.mock.calls.length
  expect((await client.cashHistory({ tableId: ' A01' })).kind).toBe('unavailable')
  expect(fetcher).toHaveBeenCalledTimes(count)
})
it.each([{ tableId: '' }, { tableId: null }, { tableId: 'x'.repeat(257) }, { orderBatchId: ' x' }, { orderBatchId: 'x\u0080y' }])('rejects noncanonical new filters %j', query => {
  expect(() => validateCashHistoryQuery(query as never)).toThrow()
})
it('verifies filtered row metadata and keeps omitted or null legacy metadata unknown', () => {
  const detail = cashItemizedDetail()
  expect(parseCashHistoryPage(cashPage([detail.transaction]), 'branch', { tableId: 'A01', orderBatchId: 'ORDER-1' }).items[0].orderBatchIds).toEqual(['ORDER-1'])
  expect(parseCashHistoryPage(cashPage(), 'branch', {}).items[0].tableId).toBeUndefined()
  expect(parseCashHistoryPage(cashPage([cashItem({ tableId: null, orderBatchIds: null })]), 'branch', {}).items[0].tableId).toBeNull()
  for (const item of [cashItem(), cashItem({ tableId: null, orderBatchIds: null }), detail.transaction]) {
    expect(() => parseCashHistoryPage(cashPage([item]), 'branch', { tableId: 'wrong' })).toThrow()
    expect(() => parseCashHistoryPage(cashPage([item]), 'branch', { orderBatchId: 'wrong' })).toThrow()
  }
  expect(() => parseCashHistoryPage(cashPage([cashItem({ orderBatchIds: ['ORDER-1', 'ORDER-1'] })]), 'branch', {})).toThrow()
})
it('reads original item, option, comment, discount and policy facts without pricing again', () => {
  const value = cashItemizedDetail()
  value.bill!.items[0].unitPrice = 50.004
  value.bill!.items[0].options[0].unitPrice = 5
  value.bill!.items[0].options.push({ ...value.bill!.items[0].options[0] })
  const parsed = parseCashHistoryDetail(value, 'branch', 'CASH-1')
  expect(parsed.bill!.paymentRequestId).toBe('PREPARE-1')
  expect(parsed.bill!.items[0].unitPrice).toBe(50.004)
  expect(parsed.bill!.items[0].grossAmount).toBe(120)
  expect(parsed.bill!.items[0].options).toHaveLength(2)
  expect(parsed.bill!.items[0].comment).toBe('ไม่เผ็ด\nแยกน้ำ')
  expect(parsed.bill!.masterDataVersion).toBe('menu-original')
  expect(parsed.financials!.amountDue).toBe(110)
})
it('does not equate rounded requested discount with its original unrounded request value', () => {
  const value = cashItemizedDetail()
  value.bill!.items[0].discount!.requestedValue = 9.995
  expect(parseCashHistoryDetail(value, 'branch', 'CASH-1').bill!.items[0].discount!.appliedAmount).toBe(10)
})
const mutations: Array<[string, (value: CashHistoryDetail) => void]> = [
  ['branch', value => { value.bill!.branchId = 'other' }],
  ['session', value => { value.bill!.tableSessionId = 'other' }],
  ['table', value => { value.bill!.tableId = 'other' }],
  ['batch summary', value => { value.transaction.orderBatchIds = ['other'] }],
  ['line duplication', value => { value.bill!.items.push({ ...value.bill!.items[0] }) }],
  ['quantity', value => { value.bill!.items[0].quantity = 0 }],
  ['quantity outside Int32', value => { value.bill!.items[0].quantity = 2147483648 }],
  ['option quantity', value => { value.bill!.items[0].options[0].quantityPerItem = 0 }],
  ['option price', value => { value.bill!.items[0].options[0].unitPrice = -1 }],
  ['name control', value => { value.bill!.items[0].displayName = 'bad\u0080name' }],
  ['comment control', value => { value.bill!.items[0].comment = 'bad\u0000comment' }],
  ['fulfillment', value => { value.bill!.items[0].fulfillmentType = 'unknown' as never }],
  ['net', value => { value.bill!.items[0].netAmount = 109 }],
  ['bill discount', value => { value.bill!.billDiscount!.appliedAmount = 4 }],
  ['requested discount', value => { value.bill!.items[0].discount!.requestedAmount = 9 }],
  ['percentage', value => { value.bill!.items[0].discount!.kind = 'Percentage'; value.bill!.items[0].discount!.requestedValue = 101 }],
  ['payment request', value => { value.bill!.paymentRequestId = '' }],
  ['financials missing', value => { value.financials = null }],
]
it.each(mutations)('rejects contradictory or malformed captured bill: %s', (_, mutate) => {
  const value = cashItemizedDetail(); mutate(value)
  expect(() => parseCashHistoryDetail(value, 'branch', 'CASH-1')).toThrow()
})
it('distinguishes old absent and explicit unavailable bills from a real recorded empty bill', () => {
  expect(parseCashHistoryDetail(cashDetail(), 'branch', 'CASH-1').bill).toBeUndefined()
  expect(parseCashHistoryDetail({ ...cashDetail(cashItem({ tableId: null, orderBatchIds: null })), bill: null }, 'branch', 'CASH-1').bill).toBeNull()
  const value = cashItemizedDetail(); value.bill!.items = []; value.bill!.billDiscount = null
  value.transaction = { ...value.transaction, orderBatchIds: [], tenderedAmount: 0, appliedAmount: 0, changeAmount: 0 }
  value.financials = { itemGrossAmount: 0, lineDiscountAmount: 0, billDiscountAmount: 0, serviceChargeAmount: 0, totalBeforeRounding: 0, roundingAdjustment: 0, amountDue: 0, vatInclusiveAmount: 0, amountBeforeVat: 0 }
  expect(parseCashHistoryDetail(value, 'branch', 'CASH-1').bill!.items).toEqual([])
  expect(() => parseCashHistoryDetail({ ...cashItemizedDetail(), bill: null }, 'branch', 'CASH-1')).toThrow()
})
it('rejects an omitted bill when modern item metadata proves a captured item list exists', () => {
  const value = cashItemizedDetail(); delete value.bill
  expect(() => parseCashHistoryDetail(value, 'branch', 'CASH-1')).toThrow()
  value.transaction.orderBatchIds = []
  expect(() => parseCashHistoryDetail(value, 'branch', 'CASH-1')).toThrow()
})
