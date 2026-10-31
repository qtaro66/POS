import type { SessionDetail } from '../api/orderingContracts.ts'
import type { OrderingReadClient } from './readController.ts'
import { createOrderingClient } from '../api/orderingClient.ts'
import type { CommandJournal, JournalEntry } from './commandJournal.ts'

/** Development-only data. This module is imported only behind Vite's DEV flag. */
export function previewDetail(tableSessionId = 'S-A04', tableId = 'A04'): SessionDetail {
  const empty = tableSessionId === 'S-A08'
  return {
    schemaVersion: 1, session: { tableSessionId, tableId, status: tableId === 'B12' ? 'PaymentPending' : 'Ordering', tableVersion: 1 },
    asOf: '2026-10-31T08:30:00Z', hasOpenCancellationRequest: false,
    billPreviewTotals: empty ? null : {
      masterDataVersion: 'PREVIEW-MASTER', calculationPolicyVersion: 'PREVIEW-CALC', vatRate: 7, serviceChargeRate: 0,
      itemGrossAmount: 530, lineDiscountAmount: 0, itemNetAmount: 530, billDiscountAmount: 0,
      serviceChargeBase: 530, serviceChargeAmount: 0, totalBeforeRounding: 530, roundingAdjustment: 0,
      netTotal: 530, vatInclusiveAmount: 34.67, amountBeforeVat: 495.33,
    },
    batches: empty ? [] : [
      { orderBatchId: `${tableSessionId}:PREVIEW-BATCH-1`, requestId: 'PREVIEW-REQUEST-1', branchSequence: 2, serverReceivedAt: '2026-10-31T08:10:00Z', acceptanceStatus: 'BranchAccepted', activeGrossAmount: 530, activeLineDiscountAmount: 0, activeItemNetAmount: 530,
        lines: [
          { orderLineId: `${tableSessionId}:L1`, lineSequence: 1, sellableItemId: 'F001', displayName: tableId === 'B12' ? 'ผัดไทยกุ้ง' : 'ข้าวผัดกุ้ง', originalQuantity: 2, activeQuantity: 2, status: 'Active', unitPrice: 185, fulfillmentType: 'DineIn', comment: 'ไม่ใส่ต้นหอม', options: [{ sellableItemId: 'TOP-1', optionGroupId: 'TOP', displayName: 'ไข่ดาว', quantityPerItem: 1, unitPrice: 15 }], activeGrossAmount: 400, activeDiscountAmount: 0, activeNetAmount: 400 },
          { orderLineId: `${tableSessionId}:L2`, lineSequence: 2, sellableItemId: 'D001', displayName: 'น้ำมะนาวโซดา', originalQuantity: 2, activeQuantity: 2, status: 'Active', unitPrice: 65, fulfillmentType: 'Takeaway', comment: 'หวานน้อย', options: [], activeGrossAmount: 130, activeDiscountAmount: 0, activeNetAmount: 130 },
        ] },
      { orderBatchId: `${tableSessionId}:PREVIEW-BATCH-2`, requestId: 'PREVIEW-REQUEST-2', branchSequence: 3, serverReceivedAt: '2026-10-31T08:20:00Z', acceptanceStatus: 'BranchAccepted', activeGrossAmount: 0, activeLineDiscountAmount: 0, activeItemNetAmount: 0,
        lines: [{ orderLineId: `${tableSessionId}:L3`, lineSequence: 1, sellableItemId: 'F002', displayName: 'ปีกไก่ทอด', originalQuantity: 1, activeQuantity: 0, status: 'Cancelled', unitPrice: 120, fulfillmentType: 'DineIn', comment: null, options: [], activeGrossAmount: 0, activeDiscountAmount: 0, activeNetAmount: 0 }] },
    ],
  }
}

export function createPreviewReadClient(): OrderingReadClient {
  const sessions = [previewDetail().session, previewDetail('S-B12', 'B12').session, previewDetail('S-A08', 'A08').session]
  return {
    async activeTables() { return { kind: 'ok', value: { schemaVersion: 1, includesAvailableTables: false, sessions: structuredClone(sessions) } } },
    async sessionDetail(id) {
      const table = sessions.find(session => session.tableSessionId === id)
      return table ? { kind: 'ok', value: previewDetail(id, table.tableId) } : { kind: 'notFound' }
    },
  }
}

/** Synthetic read/status preview; no external network, persistent storage or mutation. */
export function createPreviewRecoverySession() {
  const read = createPreviewReadClient()
  let sequence = 0
  const client = createOrderingClient({ serviceOrigin: 'https://preview.example.test', branchId: 'PREVIEW-BRANCH', deviceId: 'PREVIEW-PC', employeeId: 'PREVIEW-EMPLOYEE', deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, {
    requestId: () => `PREVIEW-RECOVERY-REQUEST-${++sequence}`,
    now: () => new Date('2026-10-31T08:30:00Z'),
    fetch: async (url, init) => {
      if (init.method !== 'GET') throw new Error('Preview mutations are disabled.')
      let body: unknown, status = 200
      if (url.endsWith('/table-sessions')) body = (await read.activeTables() as { kind: 'ok'; value: unknown }).value
      else if (url.includes('/commands/')) {
        const requestId = decodeURIComponent(url.split('/').at(-1)!)
        const accepted = requestId === 'PREVIEW-RECOVERY-REQUEST-1'
        status = accepted ? 200 : 404
        body = { result: { schemaVersion: 1, requestId, status: accepted ? 'BranchAccepted' : 'NoDurableResult', tableSessionId: accepted ? 'S-A04' : null, tableSessionStatus: accepted ? 'Ordering' : null, tableVersion: accepted ? 1 : null, errorCode: null, serverReceivedAt: accepted ? '2026-10-31T08:30:00Z' : null, branchSequence: accepted ? 4 : null }, validationDetails: null }
      } else {
        const detail = await read.sessionDetail(decodeURIComponent(url.split('/').at(-1)!))
        status = detail.kind === 'ok' ? 200 : 404
        body = detail.kind === 'ok' ? detail.value : null
      }
      return new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
    },
  })
  const entries: JournalEntry[] = [
    { command: client.snapshot(client.prepareSubmitOrder('S-A04', [{ sellableItemId: 'PREVIEW-F001', quantity: 2, fulfillmentType: 'Takeaway', comment: 'ไม่ใส่ต้นหอม', options: [{ optionGroupId: 'PREVIEW-TOPPING', sellableItemId: 'PREVIEW-EGG', quantity: 1 }] }])), state: 'pending' },
    { command: client.snapshot(client.prepareOpenTable('B12', 'PREVIEW-PROPOSED-SESSION')), state: 'pending' },
  ]
  const journal: CommandJournal = {
    async list() { return structuredClone(entries) },
    async load(_scope, requestId) { return structuredClone(entries.find(entry => entry.command.requestId === requestId) ?? null) },
    async resolve(command) { const entry = entries.find(entry => entry.command.requestId === command.requestId); if (entry) entries[entries.indexOf(entry)] = { ...entry, state: 'resolved' } },
    async add() { throw new Error('Preview mutations are disabled.') },
    async finalize() { throw new Error('Preview deletion is disabled.') },
  }
  return { client, journal }
}
