import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient } from '../api/orderingClient.ts'
import type { CommandResponse, SessionMenu } from '../api/orderingContracts.ts'
import { createBasketStore } from './basketStore.ts'
import { createBasketEditor } from './basketEditor.ts'
import { previewDetail } from './preview.ts'

/** DEV/test fixture only: isolated in-memory IndexedDB and injected HTTP, never real storage/network. */
export async function createBasketPreview() {
  const factory = new IDBFactory()
  const scope = { serviceOrigin: 'https://preview-basket.example.test', branchId: 'PREVIEW-BRANCH', deviceId: 'PREVIEW-PC', employeeId: 'PREVIEW-EMPLOYEE', tableSessionId: 'PREVIEW-BASKET-SESSION' }
  const state = { locked: false, failReads: false, menuVersion: 'PREVIEW-BASKET-MASTER', postBehavior: 'accept' as 'accept' | 'lostAfterAccept' | 'unavailable' | 'reject', postedBodies: [] as string[] }
  const availability = { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }
  const menu: SessionMenu = { schemaVersion: 1, tableSessionId: scope.tableSessionId, masterDataVersion: 'PREVIEW-BASKET-MASTER', items: [
    { sellableItemId: 'PREVIEW-F001', kind: 'Item', displayName: 'ข้าวผัดกุ้ง', unitPrice: 185, availability, optionGroups: [{ optionGroupId: 'PREVIEW-TOP', displayName: 'Topping', optionType: 'Topping', minSelections: 0, maxSelections: 3, allowRepeatedOption: true, maxQuantityPerOption: 3, options: [{ sellableItemId: 'PREVIEW-EGG', kind: 'Topping', displayName: 'ไข่ดาว', unitPrice: 15, availability, optionGroups: [] }] }] },
    { sellableItemId: 'PREVIEW-D001', kind: 'Item', displayName: 'น้ำมะนาวโซดา', unitPrice: 65, availability, optionGroups: [] },
  ] }
  const receipts = new Map<string, CommandResponse>()
  let sequence = 0
  const response = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
  const client = createOrderingClient({ ...scope, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, {
    requestId: () => `PREVIEW-BASKET-REQUEST-${++sequence}`, now: () => new Date('2026-11-01T04:00:00Z'),
    fetch: async (url, init) => {
      if (init.method === 'POST') {
        const body = init.body as string, requestId = JSON.parse(body).context.requestId
        state.postedBodies.push(body)
        if (state.postBehavior === 'unavailable') return response(null, 503)
        const rejected = state.postBehavior === 'reject'
        const receipt: CommandResponse = { result: { schemaVersion: 1, requestId, status: rejected ? 'Rejected' : 'BranchAccepted', tableSessionId: scope.tableSessionId, tableSessionStatus: 'Ordering', tableVersion: 1, errorCode: rejected ? 'CatalogValidationFailed' : null, serverReceivedAt: '2026-11-01T04:00:00Z', branchSequence: sequence + 10 }, validationDetails: null }
        receipts.set(requestId, receipt)
        if (state.postBehavior === 'lostAfterAccept') throw new Error('Synthetic lost response')
        return response(receipt, rejected ? 409 : 200)
      }
      if (url.includes('/commands/')) {
        const requestId = decodeURIComponent(url.split('/').at(-1)!), found = receipts.get(requestId)
        return found ? response(found) : response({ result: { schemaVersion: 1, requestId, status: 'NoDurableResult', tableSessionId: null, tableSessionStatus: null, tableVersion: null, errorCode: null, serverReceivedAt: null, branchSequence: null }, validationDetails: null }, 404)
      }
      if (state.failReads) return response(null, 503)
      if (url.endsWith('/menu')) return response({ ...menu, masterDataVersion: state.menuVersion })
      const detail = previewDetail(scope.tableSessionId, 'A04')
      return response({ ...detail, session: { ...detail.session, status: state.locked ? 'PaymentPending' : 'Ordering' } })
    },
  })
  const baskets = createBasketStore(factory)
  await baskets.save({ ...scope, sessionMasterDataVersion: menu.masterDataVersion, lines: [
    { sellableItemId: 'PREVIEW-F001', quantity: 1, fulfillmentType: 'DineIn', comment: 'ไม่ใส่ต้นหอม', options: [{ optionGroupId: 'PREVIEW-TOP', sellableItemId: 'PREVIEW-EGG', quantity: 1 }] },
    { sellableItemId: 'PREVIEW-D001', quantity: 2, fulfillmentType: 'Takeaway', comment: 'หวานน้อย', options: [] },
  ] }, null)
  return { controller: createBasketEditor(client, scope.tableSessionId, factory), client, factory, scope, baskets, state }
}
