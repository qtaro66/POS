import { expect, it, vi } from 'vitest'
import type { DeviceStore } from '../ports.ts'
import { createAuthenticatedBranchRequest, type FetchLike } from './http.ts'
import { createOrderingReadGateways } from './orderingReadGateways.ts'

const device = { deviceId: 'D1', deviceName: 'Demo', branchId: 'B1', branchName: 'Demo',
  branchServiceUrl: 'https://branch.test', pairedAt: '2026-11-01T00:00:00Z' }
const employee = { employeeId: 'E1', displayName: 'Demo', authenticatedAt: device.pairedAt, sessionId: 'SESSION' }
const availability = { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }
const root = { sellableItemId: 'I1', kind: 'Item', displayName: 'Demo item', unitPrice: 25.55, availability }

function gateways(fetch: FetchLike) {
  const store: DeviceStore = { load: async () => device, loadCredential: async () => 'demo-device-credential', save: async () => {} }
  return createOrderingReadGateways(createAuthenticatedBranchRequest(fetch, store, () => 'demo-session-token'))
}

it('reads only active Branch sessions with both credentials and preserves missing timestamps', async () => {
  const fetch = vi.fn<FetchLike>().mockResolvedValue({ ok: true, status: 200, json: async () => ({
    schemaVersion: 1, includesAvailableTables: false,
    sessions: [{ tableId: 'A1', tableSessionId: 'TABLE-A', tableVersion: 1, status: 'Ordering' }],
  }) })
  const { tables } = gateways(fetch)
  expect(await tables.listActiveTables(device)).toEqual({ kind: 'unreachable' })
  expect(fetch).not.toHaveBeenCalled()
  expect(await tables.listActiveTables(device, employee)).toMatchObject({ kind: 'ok', snapshot: {
    tables: [{ tableId: 'A1', tableLabel: 'A1', tableSessionId: 'TABLE-A', openedAt: null }],
  } })
  expect(fetch).toHaveBeenCalledWith('https://branch.test/api/v1/ordering/table-sessions', expect.objectContaining({
    method: 'GET', redirect: 'error', credentials: 'omit',
    headers: { Authorization: 'Device demo-device-credential', 'X-POS-Employee-Session': 'demo-session-token' },
  }))
})

it('maps session prices, Sold Out options and selection constraints without fabricating categories', async () => {
  const fetch = vi.fn<FetchLike>().mockResolvedValue({ ok: true, status: 200, json: async () => ({
    schemaVersion: 1, tableSessionId: 'A:01', masterDataVersion: 'V1',
    items: [{ ...root, optionGroups: [{
      optionGroupId: 'G1', displayName: 'Extras', optionType: 'Topping', minSelections: 0, maxSelections: 2,
      allowRepeatedOption: true, maxQuantityPerOption: 2,
      options: [{ ...root, kind: 'Topping', sellableItemId: 'T1', displayName: 'Egg', unitPrice: 0,
        availability: { ...availability, soldOut: true }, optionGroups: [] }],
    }] }, { ...root, sellableItemId: 'I2', availability: { ...availability, centralActive: false } }],
  }) })
  expect(await gateways(fetch).menu.loadMenu(device, 'A:01', employee)).toEqual({ kind: 'ok', menu: {
    masterDataVersion: 'V1', categories: [], items: [{ sellableItemId: 'I1', name: 'Demo item', price: 2555,
      code: null, categoryId: null, availability: 'available', optionGroups: [{
        optionGroupId: 'G1', name: 'Extras', optionType: 'Topping', minSelections: 0, maxSelections: 2,
        allowRepeatedOption: true, maxQuantityPerOption: 2,
        options: [{ optionId: 'T1', name: 'Egg', price: 0, availability: 'soldOut', isDefault: false }],
      }] }],
  } })
  expect(fetch).toHaveBeenCalledWith('https://branch.test/api/v1/ordering/table-sessions/A%3A01/menu', expect.anything())
})

it('rejects a mismatched session and a price the current view cannot represent exactly', async () => {
  const fetch = vi.fn<FetchLike>().mockResolvedValue({ ok: true, status: 200, json: async () => ({
    schemaVersion: 1, tableSessionId: 'A1', masterDataVersion: 'V1',
    items: [{ ...root, unitPrice: 25.555, optionGroups: [] }],
  }) })
  const { menu } = gateways(fetch)
  await expect(menu.loadMenu(device, 'B1', employee)).rejects.toThrow('Invalid Branch menu response.')
  await expect(menu.loadMenu(device, 'A1', employee)).rejects.toThrow('Unrepresentable Branch price.')
})

it('reads authoritative table amounts without exposing payment fields', async () => {
  const fetch = vi.fn<FetchLike>().mockResolvedValue({ ok: true, status: 200, json: async () => ({
    schemaVersion: 1,
    session: { tableSessionId: 'A:01', tableVersion: 3, status: 'Ordering' },
    hasOpenCancellationRequest: true,
    batches: [{ lines: [{
      orderLineId: 'L1', displayName: 'Demo item', activeQuantity: 2, unitPrice: 25,
      activeDiscountAmount: 1, activeNetAmount: 49, status: 'Active', comment: null, options: [],
    }] }],
    billPreviewTotals: {
      itemGrossAmount: 50, lineDiscountAmount: 1, billDiscountAmount: 0,
      serviceChargeAmount: 0, vatInclusiveAmount: 3.21, netTotal: 49, tenderedAmount: 100,
    },
  }) })
  const { tables } = gateways(fetch)
  expect(await tables.loadDetail?.(device, employee, 'A:01', new AbortController().signal)).toEqual({
    tableVersion: 3, status: 'Ordering', hasOpenCancellationRequest: true,
    lines: [{ id: 'L1', name: 'Demo item', quantity: 2, unitPrice: 25, discount: 1,
      total: 49, status: 'Active', comment: null, options: [] }],
    totals: { Items: 50, 'Line discounts': 1, 'Bill discount': 0, 'Service charge': 0, 'VAT included': 3.21, 'Net total': 49 },
  })
  expect(fetch).toHaveBeenCalledWith('https://branch.test/api/v1/ordering/table-sessions/A%3A01', expect.anything())
})
