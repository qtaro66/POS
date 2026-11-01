import test from 'node:test'
import assert from 'node:assert/strict'
import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient } from '../src/api/orderingClient.ts'
import { createBasketStore } from '../src/ordering/basketStore.ts'
import { createCommandJournal } from '../src/ordering/commandJournal.ts'
import { createBasketSubmission } from '../src/ordering/basketSubmission.ts'
import { previewDetail } from '../src/ordering/preview.ts'
import { commandsStore } from '../src/ordering/orderingDatabase.ts'
import type { OrderLine, SessionMenu } from '../src/api/orderingContracts.ts'

const owner = { serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01' }
const scope = { ...owner, tableSessionId: 'S-A04' }
const line: OrderLine = { sellableItemId: 'F001', quantity: 2, fulfillmentType: 'Takeaway', comment: 'ไม่เผ็ด', options: [{ optionGroupId: 'TOP', sellableItemId: 'EGG', quantity: 1 }] }
const available = { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }
function menu(): SessionMenu {
  return { schemaVersion: 1, tableSessionId: 'S-A04', masterDataVersion: 'PREVIEW-MASTER', items: [{ sellableItemId: 'F001', kind: 'Item', displayName: 'ตัวอย่างอาหาร', unitPrice: 185, availability: { ...available }, optionGroups: [{ optionGroupId: 'TOP', displayName: 'Topping', optionType: 'Topping', minSelections: 0, maxSelections: 4, allowRepeatedOption: true, maxQuantityPerOption: 3, options: [{ sellableItemId: 'EGG', kind: 'Topping', displayName: 'ไข่ดาว', unitPrice: 15, availability: { ...available }, optionGroups: [] }] }] }] }
}
function receipt(requestId: string, status = 'BranchAccepted') {
  const missing = status === 'NoDurableResult'
  return { result: { schemaVersion: 1, requestId, status, tableSessionId: missing ? null : 'S-A04', tableSessionStatus: missing ? null : 'Ordering', tableVersion: missing ? null : 1, errorCode: status === 'Rejected' ? 'CatalogValidationFailed' : null, serverReceivedAt: missing ? null : '2026-11-01T03:30:00Z', branchSequence: missing ? null : 4 }, validationDetails: null }
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
function harness(factory = new IDBFactory(), employeeId = owner.employeeId) {
  const calls: { url: string; init: RequestInit }[] = []
  const state = {
    detail: previewDetail(), menu: menu(), detailStatus: 200, menuStatus: 200,
    lookup: undefined as undefined | ((id: string) => Response),
    post: undefined as undefined | ((id: string) => Response | Promise<Response>),
    beforeDetail: undefined as undefined | (() => Promise<void>),
    beforeMenu: undefined as undefined | (() => Promise<void>),
  }
  let generated = 0
  const client = createOrderingClient({ ...owner, employeeId, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, {
    requestId: () => `REQUEST-${++generated}`, now: () => new Date('2026-11-01T03:30:00Z'),
    fetch: async (url, init) => {
      calls.push({ url, init })
      if (url.includes('/commands/')) {
        const id = decodeURIComponent(url.split('/').at(-1)!)
        return state.lookup?.(id) ?? json(receipt(id, 'NoDurableResult'), 404)
      }
      if (init.method === 'POST') {
        const id = JSON.parse(init.body as string).context.requestId
        return state.post?.(id) ?? json(receipt(id))
      }
      if (url.endsWith('/menu')) { await state.beforeMenu?.(); return json(state.menu, state.menuStatus) }
      await state.beforeDetail?.()
      return json(state.detail, state.detailStatus)
    },
  })
  const baskets = createBasketStore(factory), journal = createCommandJournal(factory)
  const service = createBasketSubmission(client, factory)
  const save = (lines: readonly OrderLine[] = [line]) => baskets.save({ ...scope, sessionMasterDataVersion: 'PREVIEW-MASTER', lines }, null)
  return { factory, calls, client, state, baskets, journal, service, save, generated: () => generated }
}
const posts = (h: ReturnType<typeof harness>) => h.calls.filter(call => call.init.method === 'POST')

test('submit checks session/menu, stages first, posts original identity and atomically applies acceptance', async () => {
  const h = harness(), draft = await h.save()
  h.state.post = async id => {
    assert.equal((await h.service.load(scope.tableSessionId))?.pendingRequestId, id)
    assert.equal((await h.journal.load(owner, id))?.state, 'pending')
    return json(receipt(id))
  }
  const result = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(result.kind, 'durable')
  if (result.kind !== 'durable') throw new Error('Expected receipt')
  assert.equal(result.localApplied, true)
  assert.equal(h.generated(), 1)
  assert.deepEqual(h.calls.map(call => call.init.method), ['GET', 'GET', 'GET', 'POST'])
  assert.deepEqual(JSON.parse(posts(h)[0].init.body as string).lines, draft.lines)
  assert.equal(await h.service.load(scope.tableSessionId), null)
  assert.deepEqual(await h.journal.list(owner), [])
})

test('zero frozen prices remain sellable, but quantities must fit the v1 integer wire fields', async () => {
  const free = harness(), draft = await free.save()
  const item = free.state.menu.items[0]
  free.state.menu = { ...free.state.menu, items: [{ ...item, unitPrice: 0, optionGroups: item.optionGroups.map(group => ({ ...group, options: group.options.map(option => ({ ...option, unitPrice: 0 })) })) }] }
  assert.equal((await free.service.submit(scope.tableSessionId, draft)).kind, 'durable')
  const huge = harness(), oversized = await huge.save([{ ...line, quantity: 2_147_483_648 }])
  assert.deepEqual(await huge.service.submit(scope.tableSessionId, oversized), { kind: 'blocked', reason: 'invalidSelection' })
  assert.equal(huge.generated(), 0)
})

test('transport authentication rejection is pending recovery rather than a durable business rejection', async () => {
  const h = harness(), draft = await h.save()
  h.state.post = () => new Response(null, { status: 401 })
  const outcome = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(outcome.kind, 'pending')
  if (outcome.kind !== 'pending') throw new Error('Expected pending command')
  assert.equal(outcome.result?.kind, 'blocked')
  assert.ok((await h.service.load(scope.tableSessionId))?.pendingRequestId)
  assert.equal(h.generated(), 1)
})

for (const status of ['PaymentPending', 'PaymentInProgress', 'PartiallyPaid'] as const) {
  test(`${status} blocks fresh submission without creating a request ID`, async () => {
    const h = harness(), draft = await h.save()
    h.state.detail = { ...h.state.detail, session: { ...h.state.detail.session, status } }
    assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: 'sessionLocked' })
    assert.equal(h.generated(), 0)
    assert.equal(h.calls.length, 1)
    assert.deepEqual(await h.service.load(scope.tableSessionId), draft)
  })
}

for (const [name, change, expected] of [
  ['changed version', (m: SessionMenu) => ({ ...m, masterDataVersion: 'NEW-MASTER' }), 'catalogChanged'],
  ['sold out parent', (m: SessionMenu) => ({ ...m, items: m.items.map(i => ({ ...i, availability: { ...i.availability, soldOut: true } })) }), 'selectionUnavailable'],
  ['missing current price', (m: SessionMenu) => ({ ...m, items: m.items.map(i => ({ ...i, availability: { ...i.availability, hasCurrentPrice: false } })) }), 'selectionUnavailable'],
  ['removed item', (m: SessionMenu) => ({ ...m, items: [] }), 'selectionUnavailable'],
] as const) {
  test(`${name} preserves the basket without staging or sending`, async () => {
    const h = harness(), draft = await h.save()
    h.state.menu = change(h.state.menu)
    assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: expected })
    assert.equal(h.generated(), 0)
    assert.equal(posts(h).length, 0)
    assert.deepEqual(await h.journal.list(owner), [])
  })
}

for (const [name, options, maximum, expected] of [
  ['duplicate option', [line.options![0], line.options![0]], 4, 'invalidSelection'],
  ['quantity above per-option maximum', [{ ...line.options![0], quantity: 4 }], 4, 'invalidSelection'],
  ['quantity above group maximum', [{ ...line.options![0], quantity: 2 }], 1, 'invalidSelection'],
  ['unknown group', [{ ...line.options![0], optionGroupId: 'UNKNOWN' }], 4, 'invalidSelection'],
] as const) {
  test(`${name} is an advisory local block, not a durable Branch rejection`, async () => {
    const h = harness(), draft = await h.save([{ ...line, options }])
    const item = h.state.menu.items[0]
    h.state.menu = { ...h.state.menu, items: [{ ...item, optionGroups: [{ ...item.optionGroups[0], maxSelections: maximum }] }] }
    assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: expected })
    assert.equal(h.generated(), 0)
    assert.equal(posts(h).length, 0)
  })
}

test('sold-out selected topping, required group and no-repeat quantity are checked without price arithmetic', async () => {
  for (const mode of ['soldOut', 'required', 'noRepeat'] as const) {
    const h = harness(), draft = await h.save(mode === 'required' ? [{ ...line, options: [] }] : [{ ...line, options: [{ ...line.options![0], quantity: 2 }] }])
    const item = h.state.menu.items[0], group = item.optionGroups[0]
    const changed = mode === 'soldOut' ? { ...group, options: group.options.map(option => ({ ...option, availability: { ...option.availability, soldOut: true } })) }
      : mode === 'required' ? { ...group, minSelections: 1 } : { ...group, allowRepeatedOption: false }
    h.state.menu = { ...h.state.menu, items: [{ ...item, optionGroups: [changed] }] }
    const result = await h.service.submit(scope.tableSessionId, draft)
    assert.equal(result.kind, 'blocked')
    assert.equal(posts(h).length, 0)
  }
})

test('closed/missing session and unavailable menu never mint replacement requests', async () => {
  for (const kind of ['missing', 'menuUnavailable'] as const) {
    const h = harness(), draft = await h.save()
    if (kind === 'missing') h.state.detailStatus = 404
    else h.state.menuStatus = 503
    assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: kind === 'missing' ? 'sessionUnavailable' : 'catalogUnavailable' })
    assert.equal(h.generated(), 0)
    assert.equal(posts(h).length, 0)
  }
})

test('empty/stale baskets and a different authenticated owner cannot submit', async () => {
  const h = harness(), empty = await h.save([])
  assert.deepEqual(await h.service.submit(scope.tableSessionId, empty), { kind: 'blocked', reason: 'emptyBasket' })
  assert.deepEqual(await h.service.submit(scope.tableSessionId, { ...empty, revision: 99 }), { kind: 'blocked', reason: 'staleBasket' })
  const other = harness(h.factory, 'OTHER-EMPLOYEE')
  assert.deepEqual(await other.service.submit(scope.tableSessionId, empty), { kind: 'blocked', reason: 'missingBasket' })
  assert.equal(h.calls.length + other.calls.length, 0)
})

test('lost POST result survives reload; recover applies acceptance even if the table is now payment-locked', async () => {
  const h = harness(), draft = await h.save()
  h.state.post = () => { throw new Error('Lost response') }
  const pending = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(pending.kind, 'pending')
  const bound = (await h.service.load(scope.tableSessionId))!
  const next = harness(h.factory)
  next.state.detail = { ...next.state.detail, session: { ...next.state.detail.session, status: 'PaymentPending' } }
  next.state.lookup = id => json(receipt(id))
  assert.deepEqual(await next.service.submit(scope.tableSessionId, bound), { kind: 'pending', requestId: bound.pendingRequestId })
  assert.equal(next.calls.length, 0)
  const recovered = await next.service.recover(scope.tableSessionId, bound)
  assert.equal(recovered.kind, 'durable')
  assert.equal(next.generated(), 0)
  assert.equal(next.calls.length, 1)
  assert.ok(next.calls[0].url.includes('/commands/'))
  assert.equal(await next.service.load(scope.tableSessionId), null)
})

async function pendingFixture() {
  const h = harness(), draft = await h.save()
  const command = h.client.snapshot(h.client.prepareSubmitOrder(scope.tableSessionId, draft.lines))
  const bound = await h.baskets.stage(scope, draft, command)
  return { ...h, bound, command }
}

test('GET-only recovery retains NoDurableResult; explicit retry rechecks and sends the exact original bytes', async () => {
  const h = await pendingFixture()
  const recovered = await h.service.recover(scope.tableSessionId, h.bound)
  assert.equal(recovered.kind, 'pending')
  assert.equal(posts(h).length, 0)
  const result = await h.service.retry(scope.tableSessionId, h.bound)
  assert.equal(result.kind, 'durable')
  assert.equal(posts(h).length, 1)
  assert.equal(posts(h)[0].init.body, h.command.body)
  assert.equal(h.generated(), 1)
})

test('pending retry queries first and blocks resend to a payment-locked session', async () => {
  const h = await pendingFixture()
  h.state.detail = { ...h.state.detail, session: { ...h.state.detail.session, status: 'PaymentPending' } }
  assert.deepEqual(await h.service.retry(scope.tableSessionId, h.bound), { kind: 'blocked', reason: 'sessionLocked', requestId: h.command.requestId })
  assert.ok(h.calls[0].url.includes('/commands/'))
  assert.equal(posts(h).length, 0)
  assert.deepEqual(await h.service.load(scope.tableSessionId), h.bound)
})

test('previously resolved request is not resent when Branch later has no durable result', async () => {
  const h = await pendingFixture()
  await h.journal.resolve(h.command)
  assert.equal((await h.service.retry(scope.tableSessionId, h.bound)).kind, 'pending')
  assert.equal(h.calls.length, 1)
  assert.equal(posts(h).length, 0)
})

test('double-click is busy; concurrent coordinators still create at most one submitted batch', async () => {
  const h = harness(), draft = await h.save()
  let release!: () => void
  const waiting = new Promise<void>(done => { release = done })
  h.state.beforeDetail = () => waiting
  const first = h.service.submit(scope.tableSessionId, draft)
  assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: 'busy' })
  const other = createBasketSubmission(h.client, h.factory).submit(scope.tableSessionId, draft)
  release()
  const results = await Promise.all([first, other])
  assert.equal(results.filter(result => result.kind === 'durable').length, 1)
  assert.equal(posts(h).length, 1)
})

test('edits during preflight cause staging conflict without posting the stale basket', async () => {
  const h = harness(), draft = await h.save()
  let updated = draft
  h.state.beforeMenu = async () => { updated = await h.baskets.save({ ...draft, lines: [{ ...line, quantity: 3 }] }, draft) }
  assert.deepEqual(await h.service.submit(scope.tableSessionId, draft), { kind: 'blocked', reason: 'localWriteFailed' })
  assert.equal(posts(h).length, 0)
  assert.deepEqual(await h.service.load(scope.tableSessionId), updated)
})

test('durable rejection is preserved and its basket remains for review, with no automatic resubmit', async () => {
  const h = harness(), draft = await h.save()
  h.state.post = id => json(receipt(id, 'Rejected'), 409)
  const result = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(result.kind, 'durable')
  if (result.kind !== 'durable') throw new Error('Expected rejection')
  assert.equal(result.response.result.status, 'Rejected')
  assert.equal(result.localApplied, true)
  assert.equal((await h.service.load(scope.tableSessionId))?.pendingRequestId, null)
  assert.deepEqual((await h.service.load(scope.tableSessionId))?.lines, draft.lines)
  assert.equal(posts(h).length, 1)
})

test('cancelled or unavailable lookups leave staged commands frozen rather than creating new IDs', async () => {
  const cancelled = harness(), draft = await cancelled.save(), abort = new AbortController()
  abort.abort()
  assert.deepEqual(await cancelled.service.submit(scope.tableSessionId, draft, abort.signal), { kind: 'blocked', reason: 'cancelled' })
  assert.equal(cancelled.generated(), 0)
  for (const mode of ['cancelledAfterStage', 'unavailable'] as const) {
    const h = harness(), basket = await h.save(), signal = new AbortController()
    h.state.lookup = () => { if (mode === 'cancelledAfterStage') signal.abort(); return new Response(null, { status: 503 }) }
    const result = await h.service.submit(scope.tableSessionId, basket, signal.signal)
    assert.equal(result.kind, 'pending')
    assert.ok((await h.service.load(scope.tableSessionId))?.pendingRequestId)
    assert.equal(posts(h).length, 0)
    assert.equal(h.generated(), 1)
  }
})

test('Branch acceptance remains durable when local atomic cleanup fails', async () => {
  const factory = new IDBFactory(), originalOpen = factory.open.bind(factory)
  factory.open = (...args) => {
    const request = originalOpen(...args)
    request.addEventListener('success', () => {
      const db = request.result, originalTransaction = db.transaction.bind(db)
      db.transaction = (...txArgs) => {
        const tx = originalTransaction(...txArgs), originalStore = tx.objectStore.bind(tx)
        tx.objectStore = name => {
          const store = originalStore(name)
          if (name === commandsStore) { const remove = store.delete.bind(store); store.delete = query => { const operation = remove(query); operation.addEventListener('success', () => tx.abort()); return operation } }
          return store
        }
        return tx
      }
    })
    return request
  }
  const h = harness(factory), draft = await h.save(), result = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(result.kind, 'durable')
  if (result.kind !== 'durable') throw new Error('Expected receipt')
  assert.equal(result.localApplied, false)
  assert.ok((await h.service.load(scope.tableSessionId))?.pendingRequestId)
  assert.equal((await h.journal.list(owner))[0].state, 'resolved')
  assert.equal(posts(h).length, 1)
})

test('failure to record the resolved marker preserves the durable result and pending basket', async () => {
  const factory = new IDBFactory(), originalOpen = factory.open.bind(factory)
  factory.open = (...args) => {
    const request = originalOpen(...args)
    request.addEventListener('success', () => {
      const db = request.result, originalTransaction = db.transaction.bind(db)
      db.transaction = (...txArgs) => {
        const tx = originalTransaction(...txArgs), originalStore = tx.objectStore.bind(tx)
        tx.objectStore = name => {
          const store = originalStore(name)
          if (name === commandsStore) { const put = store.put.bind(store); store.put = value => { const operation = put(value); if (value.state === 'resolved') operation.addEventListener('success', () => tx.abort()); return operation } }
          return store
        }
        return tx
      }
    })
    return request
  }
  const h = harness(factory), draft = await h.save(), result = await h.service.submit(scope.tableSessionId, draft)
  assert.equal(result.kind, 'durable')
  if (result.kind !== 'durable') throw new Error('Expected receipt')
  assert.equal(result.localApplied, false)
  assert.ok((await h.service.load(scope.tableSessionId))?.pendingRequestId)
  assert.equal((await h.journal.list(owner))[0].state, 'pending')
  assert.equal(posts(h).length, 1)
})
