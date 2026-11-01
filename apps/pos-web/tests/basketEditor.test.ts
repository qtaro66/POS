import test from 'node:test'
import assert from 'node:assert/strict'
import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient } from '../src/api/orderingClient.ts'
import { createBasketEditor } from '../src/ordering/basketEditor.ts'
import { createBasketStore } from '../src/ordering/basketStore.ts'
import { createCommandJournal } from '../src/ordering/commandJournal.ts'
import { previewDetail } from '../src/ordering/preview.ts'
import type { OrderLine, SessionMenu } from '../src/api/orderingContracts.ts'

const identity = { serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01' }
const scope = { ...identity, tableSessionId: 'S-A04' }
const selection = { sellableItemId: 'F001', quantity: 1, comment: 'ไม่เผ็ด', options: [] }
function deferred<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
function receipt(id: string, accepted = true) {
  return { result: { schemaVersion: 1, requestId: id, status: accepted ? 'BranchAccepted' : 'NoDurableResult', tableSessionId: accepted ? 'S-A04' : null, tableSessionStatus: accepted ? 'Ordering' : null, tableVersion: accepted ? 1 : null, serverReceivedAt: accepted ? '2026-11-01T04:00:00Z' : null, errorCode: null, branchSequence: accepted ? 4 : null }, validationDetails: null }
}
function harness(factory = new IDBFactory(), employeeId = identity.employeeId) {
  const state = {
    detail: previewDetail(), detailStatus: 200, menuStatus: 200, accepted: false, losePost: false,
    menu: { schemaVersion: 1, tableSessionId: 'S-A04', masterDataVersion: 'PREVIEW-MASTER', items: [{ sellableItemId: 'F001', kind: 'Item', displayName: 'ตัวอย่างอาหาร', unitPrice: 185, availability: { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }, optionGroups: [] }] } as SessionMenu,
    holdReads: undefined as Promise<void> | undefined,
    onRead: undefined as (() => void) | undefined,
    holdPost: undefined as Promise<void> | undefined,
    onPost: undefined as (() => void) | undefined,
  }
  const calls: { url: string; init: RequestInit }[] = []
  let ids = 0
  const client = createOrderingClient({ ...identity, employeeId, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, {
    requestId: () => `REQUEST-${++ids}`, fetch: async (url, init) => {
      calls.push({ url, init })
      if (init.method === 'POST') { state.onPost?.(); await state.holdPost; if (state.losePost) throw new Error('Lost response'); state.accepted = true; return json(receipt(JSON.parse(init.body as string).context.requestId)) }
      if (url.includes('/commands/')) { const id = url.split('/').at(-1)!; return json(receipt(id, state.accepted), state.accepted ? 200 : 404) }
      const value = url.endsWith('/menu') ? state.menu : state.detail, status = url.endsWith('/menu') ? state.menuStatus : state.detailStatus
      state.onRead?.(); await state.holdReads
      return json(value, status)
    },
  })
  return { factory, state, calls, client, editor: createBasketEditor(client, scope.tableSessionId, factory), baskets: createBasketStore(factory), journal: createCommandJournal(factory) }
}

test('construction does no I/O; refresh does not create an empty persisted basket or query commands', async () => {
  const h = harness()
  assert.equal(h.calls.length, 0)
  assert.equal(h.editor.getSnapshot().phase, 'idle')
  await h.editor.refresh()
  assert.equal(h.editor.getSnapshot().phase, 'ready')
  assert.equal(h.editor.getSnapshot().basket, null)
  assert.equal(h.editor.getSnapshot().issue, null)
  assert.equal(await h.baskets.load(scope), null)
  assert.equal(h.calls.length, 2)
})

test('dine-in is the default; changing new fulfillment affects only subsequently persisted lines', async () => {
  const h = harness()
  await h.editor.refresh()
  const first = h.editor.addLine(selection)
  assert.equal(h.editor.getSnapshot().activity, 'saving')
  assert.equal(h.editor.getSnapshot().basket, null)
  h.editor.setNewFulfillment('Takeaway')
  assert.equal(await first, true)
  assert.equal(await h.editor.addLine(selection), true)
  assert.deepEqual(h.editor.getSnapshot().basket?.lines.map(line => line.fulfillmentType), ['DineIn', 'Takeaway'])
  const reloaded = createBasketEditor(h.client, scope.tableSessionId, h.factory)
  await reloaded.refresh()
  assert.deepEqual(reloaded.getSnapshot().basket?.lines, h.editor.getSnapshot().basket?.lines)
  assert.equal(reloaded.getSnapshot().newFulfillment, 'DineIn')
  assert.equal(h.calls.filter(call => call.init.method === 'POST').length, 0)
})

test('replace/remove persist whole draft revisions without changing master data or other lines', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection); await h.editor.addLine(selection)
  const changed: OrderLine = { ...selection, quantity: 3, fulfillmentType: 'Takeaway', comment: 'หวานน้อย' }
  assert.equal(await h.editor.replaceLine(0, changed), true)
  assert.equal(h.editor.getSnapshot().basket?.lines[1].quantity, 1)
  assert.equal(h.editor.getSnapshot().basket?.sessionMasterDataVersion, 'PREVIEW-MASTER')
  assert.equal(await h.editor.removeLine(1), true)
  assert.equal(await h.editor.removeLine(0), true)
  assert.deepEqual(h.editor.getSnapshot().basket?.lines, [])
  assert.equal(await h.editor.replaceLine(-1, changed), false)
  assert.equal(await h.editor.removeLine(0), false)
  assert.deepEqual(await h.journal.list(identity), [])
})

test('invalid lines and Sold Out additions are rejected without changing the committed basket', async () => {
  const h = harness()
  await h.editor.refresh()
  assert.equal(await h.editor.addLine({ ...selection, quantity: 0 }), false)
  assert.equal(h.editor.getSnapshot().editError, 'invalidLine')
  h.state.menu = { ...h.state.menu, items: h.state.menu.items.map(item => ({ ...item, availability: { ...item.availability, soldOut: true } })) }
  await h.editor.refresh()
  assert.equal(await h.editor.addLine(selection), false)
  assert.equal(h.editor.getSnapshot().editError, 'selectionUnavailable')
  assert.equal(await h.baskets.load(scope), null)
})

test('removing unavailable old lines remains possible even when other old lines are unavailable', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection); await h.editor.addLine(selection)
  h.state.menu = { ...h.state.menu, items: [] }
  await h.editor.refresh()
  assert.equal(await h.editor.removeLine(0), true)
  assert.equal(h.editor.getSnapshot().basket?.lines.length, 1)
  assert.equal(await h.editor.removeLine(0), true)
})

for (const mode of ['locked', 'versionChanged', 'unavailable'] as const) {
  test(`${mode} preserves original saved data and blocks edits`, async () => {
    const h = harness()
    await h.editor.refresh(); await h.editor.addLine(selection)
    const saved = h.editor.getSnapshot().basket
    if (mode === 'locked') h.state.detail = { ...h.state.detail, session: { ...h.state.detail.session, status: 'PaymentPending' } }
    else if (mode === 'versionChanged') h.state.menu = { ...h.state.menu, masterDataVersion: 'NEW-MASTER' }
    else h.state.menuStatus = 503
    await h.editor.refresh()
    assert.equal(h.editor.getSnapshot().issue, mode === 'locked' ? 'sessionLocked' : mode === 'versionChanged' ? 'catalogChanged' : 'catalogUnavailable')
    assert.deepEqual(h.editor.getSnapshot().basket, saved)
    assert.equal(await h.editor.addLine(selection), false)
    assert.equal(await h.editor.removeLine(0), false)
    assert.equal(await h.editor.submit(), null)
    assert.deepEqual(await h.baskets.load(scope), saved)
  })
}

test('a stale-tab write failure retains the committed view and requires refresh before editing', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  const saved = h.editor.getSnapshot().basket!
  const updated = await h.baskets.save({ ...saved, lines: [{ ...saved.lines[0], quantity: 4 }] }, saved)
  assert.equal(await h.editor.addLine(selection), false)
  assert.equal(h.editor.getSnapshot().issue, 'localWriteFailed')
  assert.deepEqual(h.editor.getSnapshot().basket, saved)
  assert.deepEqual(await h.baskets.load(scope), updated)
  assert.equal(await h.editor.removeLine(0), false)
  await h.editor.refresh()
  assert.deepEqual(h.editor.getSnapshot().basket, updated)
})

test('only one save runs at a time; unmount/cancel ignores obsolete read results and can restart', async () => {
  const h = harness()
  await h.editor.refresh()
  const first = h.editor.addLine(selection)
  assert.equal(await h.editor.addLine(selection), false)
  assert.equal(await first, true)
  const waiting = deferred<void>()
  const started = deferred<void>()
  h.state.holdReads = waiting.promise
  h.state.onRead = () => started.resolve()
  const refresh = h.editor.refresh()
  await started.promise
  h.editor.cancel()
  waiting.resolve(); await refresh
  assert.equal(h.editor.getSnapshot().phase, 'loading')
  h.state.holdReads = undefined
  await h.editor.refresh()
  assert.equal(h.editor.getSnapshot().phase, 'ready')
  assert.equal(h.editor.getSnapshot().basket?.lines.length, 1)
})

test('a late superseded menu response cannot overwrite fresh session-version checks', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  const waiting = deferred<void>(), started = deferred<void>()
  h.state.holdReads = waiting.promise; h.state.onRead = () => started.resolve()
  const older = h.editor.refresh()
  await started.promise
  h.state.menu = { ...h.state.menu, masterDataVersion: 'NEW-MASTER' }
  h.state.holdReads = undefined
  await h.editor.refresh()
  const fresh = h.editor.getSnapshot()
  assert.equal(fresh.issue, 'catalogChanged')
  waiting.resolve(); await older
  assert.equal(h.editor.getSnapshot(), fresh)
})

test('a replacement authenticated editor starts empty and cannot inherit another employee basket', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  const saved = h.editor.getSnapshot().basket
  const next = harness(h.factory, 'OTHER-EMPLOYEE')
  assert.equal(next.editor.getSnapshot().basket, null)
  await next.editor.refresh()
  assert.equal(next.editor.getSnapshot().basket, null)
  await next.editor.addLine(selection)
  assert.equal(next.editor.getSnapshot().basket?.employeeId, 'OTHER-EMPLOYEE')
  assert.deepEqual(await h.baskets.load(scope), saved)
})

test('unavailable storage is an explicit failure rather than an editable empty basket', async () => {
  const h = harness(), editor = createBasketEditor(h.client, scope.tableSessionId, undefined)
  await editor.refresh()
  assert.equal(editor.getSnapshot().issue, 'storageUnavailable')
  assert.equal(await editor.addLine(selection), false)
  assert.equal(h.calls.length, 0)
})

test('cancelling an in-flight submission ignores UI completion while preserving its staged request', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  const waiting = deferred<void>(), started = deferred<void>()
  h.state.holdPost = waiting.promise; h.state.onPost = () => started.resolve(); h.state.losePost = true
  const sending = h.editor.submit()
  await started.promise
  h.editor.cancel()
  waiting.resolve(); await sending
  assert.equal(h.editor.getSnapshot().outcome, null)
  await h.editor.refresh()
  assert.ok(h.editor.getSnapshot().basket?.pendingRequestId)
  assert.equal(await h.editor.addLine(selection), false)
  assert.equal(h.calls.filter(call => call.init.method === 'POST').length, 1)
})

test('submission applies acceptance, keeps its durable outcome and requires fresh Branch data before more edits', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  const result = await h.editor.submit()
  assert.equal(result?.kind, 'durable')
  assert.equal(h.editor.getSnapshot().outcome, result)
  assert.equal(h.editor.getSnapshot().basket, null)
  assert.equal(h.editor.getSnapshot().issue, 'refreshRequired')
  assert.equal(await h.editor.addLine(selection), false)
  await h.editor.refresh()
  assert.equal(await h.editor.addLine(selection), true)
  assert.equal(h.calls.filter(call => call.init.method === 'POST').length, 1)
})

test('reload restores unknown submissions frozen; GET recovery works even when current table is locked', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  h.state.losePost = true
  assert.equal((await h.editor.submit())?.kind, 'pending')
  const bound = h.editor.getSnapshot().basket!
  assert.ok(bound.pendingRequestId)
  await h.editor.refresh()
  assert.equal(await h.editor.addLine(selection), false)
  assert.equal(await h.editor.removeLine(0), false)
  h.state.accepted = true
  h.state.detail = { ...h.state.detail, session: { ...h.state.detail.session, status: 'PaymentPending' } }
  const reloaded = createBasketEditor(h.client, scope.tableSessionId, h.factory)
  await reloaded.refresh()
  const posts = h.calls.filter(call => call.init.method === 'POST').length
  assert.equal((await reloaded.recover())?.kind, 'durable')
  assert.equal(reloaded.getSnapshot().basket, null)
  assert.equal(h.calls.filter(call => call.init.method === 'POST').length, posts)
})

test('retry is explicit and retains original bytes, while editable baskets expose no retry action', async () => {
  const h = harness()
  await h.editor.refresh(); await h.editor.addLine(selection)
  assert.equal(await h.editor.retry(), null)
  h.state.losePost = true
  await h.editor.submit()
  const firstBody = h.calls.find(call => call.init.method === 'POST')!.init.body
  h.state.losePost = false
  assert.equal((await h.editor.retry())?.kind, 'durable')
  assert.equal(h.calls.filter(call => call.init.method === 'POST')[1].init.body, firstBody)
})
