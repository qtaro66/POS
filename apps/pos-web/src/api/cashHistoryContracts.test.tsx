import { expect, it, vi } from 'vitest'
import { createOrderingClient } from './orderingClient.ts'
import { parseCashHistoryDetail, parseCashHistoryPage, validateCashHistoryQuery } from './cashHistoryContracts.ts'
import { cashDetail, cashItem, cashPage } from './cashHistoryTestFixtures.ts'

it('authenticates bounded history GETs and encodes text identities and literal search values', async () => {
  const fetcher = vi.fn(async (url: string | URL | Request) => new Response(JSON.stringify(String(url).includes('/cash-sales/') ? cashDetail(cashItem({ requestId: 'CASH/1' })) : cashPage()), { status: 200, headers: { "Content-Type": "application/json" } }))
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example', branchId: 'branch', employeeId: 'employee', deviceId: 'device', deviceCredential: 'credential', employeeSessionToken: 'token' }, { fetch: fetcher as typeof fetch })
  expect((await client.cashHistory({ receiptNumber: 'TEST-%_', paymentMethod: 'Cash', status: 'Completed', cursor: 'cursor', limit: 5 })).kind).toBe('unavailable')
  const [url, options] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
  const parameters = new URL(url).searchParams
  expect(parameters.get('receiptNumber')).toBe('TEST-%_'); expect(parameters.get('cursor')).toBe('cursor')
  expect(options.method).toBe('GET'); expect(options.body).toBeUndefined()
  expect(JSON.stringify(options.headers)).toContain('token')
  expect((await client.cashHistoryDetail('CASH/1')).kind).toBe('ok')
  expect(String(fetcher.mock.calls[1][0])).toContain('CASH%2F1')
  const count = fetcher.mock.calls.length
  expect((await client.cashHistory({ tableSessionId: ' invalid ' })).kind).toBe('unavailable')
  expect(fetcher).toHaveBeenCalledTimes(count)
})
it('accepts legacy text IDs, pinned effective dates, zero cash and captured fractional monetary values', () => {
  expect(parseCashHistoryPage(cashPage(), 'branch', {}).items[0].tableSessionId).toBe('SESSION-1')
  expect(parseCashHistoryPage(cashPage([cashItem({ tenderedAmount: 0, appliedAmount: 0, changeAmount: 0 })]), 'branch', {}).items[0].appliedAmount).toBe(0)
  expect(parseCashHistoryPage(cashPage([cashItem({ tenderedAmount: 0.3, appliedAmount: 0.1, changeAmount: 0.2 })]), 'branch', {}).items).toHaveLength(1)
  expect(parseCashHistoryDetail(cashDetail(), 'branch', 'CASH-1').financials?.amountDue).toBe(110)
  expect(parseCashHistoryDetail({ ...cashDetail(), financials: null }, 'branch', 'CASH-1').financials).toBeNull()
})
it.each([
  { businessDate: '2026-02-30' }, { receiptNumber: '' }, { receiptNumber: ' x' }, { receiptNumber: 'x'.repeat(129) },
  { tableSessionId: 'x\n' }, { receiptNumber: 'x\u0080y' }, { tableSessionId: 'x\u009fy' }, { paymentMethod: 'cash' }, { paymentMethod: null }, { status: 'Refunded' }, { limit: 0 }, { limit: null }, { cursor: 'not/base64' },
])('rejects invalid canonical queries %j', query => { expect(() => validateCashHistoryQuery(query as never)).toThrow() })
it.each([
  { branchId: 'other' }, { schemaVersion: 2 }, { businessDate: '2026-02-30' }, { nextCursor: 'bad/' },
  { items: [cashItem(), cashItem()] }, { items: [cashItem({ businessDate: '2026-11-23' })] },
  { items: [cashItem({ appliedAmount: -1 })] }, { items: [cashItem({ changeAmount: 9 })] }, { items: [cashItem({ paymentMethod: 'Card' as never })] },
  { items: [cashItem({ capturedPrintStatus: 'Printed' as never })] }, { items: [cashItem({ capturedAt: 'bad' })] }, { items: [cashItem({ employeeId: 'x\u0080y' })] },
])('rejects ambiguous or foreign history pages %j', change => { expect(() => parseCashHistoryPage({ ...cashPage(), ...change }, 'branch', {})).toThrow() })
it('requires returned rows to match the effective day, literal receipt filter and table filter', () => {
  for (const query of [{ businessDate: '2026-11-23' }, { receiptNumber: '%_' }, { tableSessionId: 'other' }]) expect(() => parseCashHistoryPage(cashPage(), 'branch', query)).toThrow()
  expect(() => parseCashHistoryPage(cashPage([], 'cursor'), 'branch', {})).toThrow()
})
it('rejects wrong detail identity and inconsistent captured financials instead of recomputing', () => {
  expect(() => parseCashHistoryDetail(cashDetail(), 'branch', 'OTHER')).toThrow()
  for (const change of [{ amountDue: 111 }, { roundingAdjustment: 1 }, { amountBeforeVat: 100 }, { serviceChargeAmount: -1 }]) {
    const detail = cashDetail(); expect(() => parseCashHistoryDetail({ ...detail, financials: { ...detail.financials, ...change } }, 'branch', 'CASH-1')).toThrow()
  }
})
