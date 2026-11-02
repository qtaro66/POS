import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient } from '../api/orderingClient.ts'
import type { AcceptedBatch, AcceptedLine, CommandResponse, MenuItem, OrderLine, SessionMenu } from '../api/orderingContracts.ts'
import { createBasketStore } from './basketStore.ts'
import { previewDetail } from './preview.ts'

/** DEV/test-only simulated Branch. No real network, browser database or hardware. */
export function createOrderingPreview(options: { factory?: IDBFactory; employeeId?: string } = {}) {
  const factory = options.factory ?? new IDBFactory()
  const scope = { serviceOrigin: 'https://preview-ordering.example.test', branchId: 'PREVIEW-BRANCH', deviceId: 'PREVIEW-PC', employeeId: options.employeeId ?? 'PREVIEW-EMPLOYEE', tableSessionId: 'PREVIEW-ORDERING-SESSION' }
  const availability = { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }
  const topping: MenuItem = { sellableItemId: 'DEMO-EGG', kind: 'Topping', displayName: 'ไข่ดาว', unitPrice: 15, availability, optionGroups: [] }
  const toppings = { optionGroupId: 'DEMO-TOPPING', displayName: 'เพิ่มพิเศษ', optionType: 'Topping' as const, minSelections: 0, maxSelections: 2, allowRepeatedOption: true, maxQuantityPerOption: 2, options: [topping] }
  const menu: SessionMenu = { schemaVersion: 1, tableSessionId: scope.tableSessionId, masterDataVersion: 'DEMO-MASTER', items: [
    { sellableItemId: 'DEMO-RICE', kind: 'Item', displayName: 'ข้าวผัดกุ้ง', unitPrice: 185, availability, optionGroups: [toppings] },
    { sellableItemId: 'DEMO-STIR', kind: 'Item', displayName: 'ผัดเปรี้ยวหวาน', unitPrice: 0, availability, optionGroups: [
      { optionGroupId: 'DEMO-SUB', displayName: 'วัตถุดิบหลัก', optionType: 'SubItem', minSelections: 1, maxSelections: 1, allowRepeatedOption: false, maxQuantityPerOption: 1, options: [
        { sellableItemId: 'DEMO-CHICKEN', kind: 'SubItem', displayName: 'ไก่ชิ้น', unitPrice: 160, availability, optionGroups: [] },
        { sellableItemId: 'DEMO-TOFU', kind: 'SubItem', displayName: 'เต้าหู้', unitPrice: 140, availability, optionGroups: [] },
      ] }, toppings,
    ] },
    { sellableItemId: 'DEMO-WATER', kind: 'Item', displayName: 'น้ำเปล่า', unitPrice: 20, availability, optionGroups: [] },
    { sellableItemId: 'DEMO-CRAB', kind: 'Item', displayName: 'ข้าวผัดปู', unitPrice: 200, availability: { ...availability, soldOut: true }, optionGroups: [] },
  ] }
  const state = { menu, locked: false, failReads: false, postBehavior: 'accept' as 'accept' | 'lostAfterAccept' | 'unavailable' | 'reject', postedBodies: [] as string[], detailReads: 0 }
  const receipts = new Map<string, CommandResponse>(), batches: AcceptedBatch[] = []
  const reply = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
  const detail = () => {
    const base = previewDetail(scope.tableSessionId, 'A04'), total = batches.reduce((sum, batch) => sum + batch.activeItemNetAmount, 0)
    return { ...base, asOf: '2026-11-01T09:31:00Z', session: { ...base.session, tableVersion: batches.length + 1, status: state.locked ? 'PaymentPending' : 'Ordering' }, batches,
      billPreviewTotals: !batches.length ? null : { ...base.billPreviewTotals!, masterDataVersion: state.menu.masterDataVersion, itemGrossAmount: total, itemNetAmount: total, serviceChargeBase: total, totalBeforeRounding: total, netTotal: total, vatInclusiveAmount: total * 7 / 107, amountBeforeVat: total * 100 / 107 } }
  }
  let sequence = 0
  const client = createOrderingClient({ ...scope, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, {
    requestId: () => `DEMO-REQUEST-${++sequence}`, now: () => new Date('2026-11-01T09:30:00Z'),
    fetch: async (url, init) => {
      if (init.method === 'POST') {
        const body = init.body as string, command = JSON.parse(body), requestId: string = command.context.requestId
        state.postedBodies.push(body)
        if (state.postBehavior === 'unavailable') return reply(null, 503)
        if (!receipts.has(requestId)) {
          const rejected = state.postBehavior === 'reject'
          const receipt: CommandResponse = { result: { schemaVersion: 1, requestId, status: rejected ? 'Rejected' : 'BranchAccepted', tableSessionId: scope.tableSessionId, tableSessionStatus: 'Ordering', tableVersion: batches.length + 2, branchSequence: batches.length + 1, serverReceivedAt: '2026-11-01T09:30:00Z', errorCode: rejected ? 'CatalogValidationFailed' : null }, validationDetails: null }
          receipts.set(requestId, receipt)
          if (!rejected) {
            const lines: AcceptedLine[] = command.lines.map((line: OrderLine, index: number) => {
              const item = state.menu.items.find(value => value.sellableItemId === line.sellableItemId)!
              const selected = (line.options ?? []).map(option => {
                const value = item.optionGroups.find(group => group.optionGroupId === option.optionGroupId)!.options.find(value => value.sellableItemId === option.sellableItemId)!
                return { sellableItemId: value.sellableItemId, optionGroupId: option.optionGroupId, displayName: value.displayName, quantityPerItem: option.quantity, unitPrice: value.unitPrice! }
              })
              const amount = (item.unitPrice! + selected.reduce((sum, value) => sum + value.unitPrice * value.quantityPerItem, 0)) * line.quantity
              return { orderLineId: `${requestId}-LINE-${index}`, lineSequence: index + 1, sellableItemId: item.sellableItemId, displayName: item.displayName, originalQuantity: line.quantity, activeQuantity: line.quantity, status: 'Active', unitPrice: item.unitPrice!, fulfillmentType: line.fulfillmentType, comment: line.comment ?? null, options: selected, activeGrossAmount: amount, activeDiscountAmount: 0, activeNetAmount: amount }
            })
            const amount = lines.reduce((sum, line) => sum + line.activeNetAmount, 0)
            batches.push({ orderBatchId: `DEMO-BATCH-${batches.length + 1}`, requestId, branchSequence: batches.length + 1, serverReceivedAt: '2026-11-01T09:30:00Z', acceptanceStatus: 'BranchAccepted', lines, activeGrossAmount: amount, activeLineDiscountAmount: 0, activeItemNetAmount: amount })
          }
        }
        if (state.postBehavior === 'lostAfterAccept') throw new Error('Synthetic lost acknowledgement')
        const receipt = receipts.get(requestId)!
        return reply(receipt, receipt.result.status === 'Rejected' ? 409 : 200)
      }
      if (url.includes('/commands/')) {
        const requestId = decodeURIComponent(url.split('/').at(-1)!), receipt = receipts.get(requestId)
        return receipt ? reply(receipt) : reply({ result: { schemaVersion: 1, requestId, status: 'NoDurableResult', tableSessionId: null, tableSessionStatus: null, tableVersion: null, branchSequence: null, serverReceivedAt: null, errorCode: null }, validationDetails: null }, 404)
      }
      if (state.failReads) return reply(null, 503)
      if (url.includes('/print-attention')) {
        const next = url.includes('?cursor=')
        return reply({ schemaVersion: 1, observedAt: '2026-11-01T09:30:00Z', totalCount: 2, nextCursor: next ? null : 'DEMO-NEXT', items: [{
          printJobId: next ? 'DEMO-PRINT-2' : 'DEMO-PRINT-1', requestId: 'DEMO-REQUEST-PRINT', tableSessionId: scope.tableSessionId,
          snapshotId: 'DEMO-PRINT-SNAPSHOT', purpose: next ? 'BillPreview' : 'KitchenOrder', targetStation: next ? 'Cashier' : 'DEMO-KITCHEN',
          status: next ? 'Failed' : 'Unknown', createdAt: '2026-11-01T09:00:00Z', assignedPrinterId: 'DEMO-PRINTER', nextRetryAt: null,
          attentionReason: next ? 'SendFailed' : 'SendOutcomeUnknown', latestAttemptStatus: next ? 'Failed' : 'Unknown', latestAttemptFinishedAt: '2026-11-01T09:01:00Z',
        }] })
      }
      if (url.endsWith('/employee-data-sync-status')) return reply({ schemaVersion: 1, lastSynchronizedAt: '2026-11-01T08:45:00Z' })
      if (url.endsWith('/cloud-data-sent-status')) return reply({ schemaVersion: 1, lastSentAt: '2026-11-01T09:00:00Z' })
      if (url.endsWith('/menu')) return reply(state.menu)
      state.detailReads++
      return url.endsWith('/table-sessions') ? reply({ schemaVersion: 1, includesAvailableTables: false, sessions: [detail().session] }) : reply(detail())
    },
  })
  return { client, factory, scope, state, baskets: createBasketStore(factory) }
}
