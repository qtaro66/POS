import test from 'node:test'
import assert from 'node:assert/strict'
import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient } from '../src/api/orderingClient.ts'
import { createBasketStore } from '../src/ordering/basketStore.ts'
import { createCommandJournal } from '../src/ordering/commandJournal.ts'
import { createCommandRecovery } from '../src/ordering/commandRecovery.ts'
import { basketsStore, commandsStore, orderingDatabaseName } from '../src/ordering/orderingDatabase.ts'

const identity = { serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01' }
const scope = { ...identity, tableSessionId: 'SESSION-01' }
const line = { sellableItemId: 'ITEM-01', quantity: 2, fulfillmentType: 'Takeaway' as const, comment: 'ไม่เผ็ด', options: [{ optionGroupId: 'TOP', sellableItemId: 'EGG', quantity: 1 }] }
const input = () => ({ ...scope, sessionMasterDataVersion: 'MASTER-01', lines: structuredClone([line]) })
function client(replies: (Response | Error)[] = [], requestId = 'REQUEST-01', overrides = {}) {
  const calls: RequestInit[] = []
  const api = createOrderingClient({ ...identity, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb', ...overrides }, {
    requestId: () => requestId, now: () => new Date('2026-10-31T10:00:00Z'),
    fetch: async (_url, init) => {
      calls.push(init)
      const reply = replies.shift()
      if (reply instanceof Error) throw reply
      assert.ok(reply, 'Unexpected network operation')
      return reply
    },
  })
  const snapshot = api.snapshot(api.prepareSubmitOrder(scope.tableSessionId, [line]))
  return { api, snapshot, calls }
}
function receipt(status = 'BranchAccepted') {
  const missing = status === 'NoDurableResult'
  return { result: { schemaVersion: 1, requestId: 'REQUEST-01', status, tableSessionId: missing ? null : 'SESSION-01', tableSessionStatus: missing ? null : 'Ordering', tableVersion: missing ? null : 1, errorCode: status === 'Rejected' ? 'SoldOut' : null, serverReceivedAt: missing ? null : '2026-10-31T10:00:00Z', branchSequence: missing ? null : 1 }, validationDetails: null }
}
const json = (value: unknown, status = 200) => new Response(JSON.stringify(value), { status, headers: { 'Content-Type': 'application/json' } })
async function open(factory: IDBFactory, version?: number): Promise<IDBDatabase> {
  return new Promise((resolve, reject) => { const request = factory.open(orderingDatabaseName, version); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error) })
}
async function write(db: IDBDatabase, store: string, value: unknown) {
  await new Promise<void>((resolve, reject) => { const tx = db.transaction(store, 'readwrite'); tx.objectStore(store).put(value); tx.oncomplete = () => resolve(); tx.onabort = tx.onerror = () => reject(tx.error) })
}

test('basket survives reload, copies input and strips caller price/token fields', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), draft = { ...input(), deviceCredential: 'forbidden', lines: [{ ...line, unitPrice: 1 }] }
  const saved = await store.save(draft, null)
  draft.lines[0].quantity = 9
  const reloaded = await createBasketStore(factory).load(scope)
  assert.deepEqual(reloaded, saved)
  assert.equal(reloaded?.lines[0].quantity, 2)
  assert.ok(!JSON.stringify(reloaded).includes('unitPrice') && !JSON.stringify(reloaded).includes('forbidden'))
  assert.equal(reloaded?.sessionMasterDataVersion, 'MASTER-01')
})

test('owner and session partitioning isolates employee, device, branch and origin', async () => {
  const store = createBasketStore(new IDBFactory())
  await store.save(input(), null)
  for (const changed of [{ employeeId: 'OTHER' }, { deviceId: 'OTHER' }, { branchId: 'OTHER' }, { serviceOrigin: 'https://other.example.test' }, { tableSessionId: 'OTHER' }]) assert.equal(await store.load({ ...scope, ...changed }), null)
  assert.equal((await store.list(identity)).length, 1)
  assert.deepEqual(await store.list({ ...identity, employeeId: 'OTHER' }), [])
})

test('compare-and-swap prevents concurrent tabs from silently overwriting a basket', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory)
  const saved = await store.save(input(), null)
  const results = await Promise.allSettled([store.save({ ...input(), lines: [{ ...line, quantity: 3 }] }, saved), createBasketStore(factory).save({ ...input(), lines: [{ ...line, quantity: 4 }] }, saved)])
  assert.equal(results.filter(result => result.status === 'fulfilled').length, 1)
  assert.equal(results.filter(result => result.status === 'rejected').length, 1)
  assert.equal((await store.load(scope))?.revision, 2)
  await assert.rejects(store.save(input(), null))
  await assert.rejects(store.remove(scope, saved))
})

test('empty editable baskets can be saved/deleted but session master version cannot be replaced', async () => {
  const store = createBasketStore(new IDBFactory())
  const saved = await store.save({ ...input(), lines: [] }, null)
  await assert.rejects(store.save({ ...input(), sessionMasterDataVersion: 'NEW-MASTER' }, saved))
  await store.remove(scope, saved)
  assert.equal(await store.load(scope), null)
})

test('a stale tab cannot overwrite or stage a deleted and recreated basket at the same scope', async () => {
  const store = createBasketStore(new IDBFactory()), h = client()
  const original = await store.save(input(), null)
  await store.remove(scope, original)
  const recreated = await store.save(input(), null)
  assert.equal(recreated.revision, original.revision)
  assert.notEqual(recreated.basketId, original.basketId)
  await assert.rejects(store.save(input(), original))
  await assert.rejects(store.remove(scope, original))
  await assert.rejects(store.stage(scope, original, h.snapshot))
  assert.deepEqual(await store.load(scope), recreated)
})

test('version tokens are snapshotted before asynchronous work', async () => {
  const store = createBasketStore(new IDBFactory())
  const original = await store.save(input(), null)
  const updated = await store.save({ ...input(), lines: [{ ...line, quantity: 3 }] }, original)
  const mutable = { ...original }
  const staleSave = store.save(input(), mutable)
  mutable.revision = updated.revision
  await assert.rejects(staleSave)
  assert.deepEqual(await store.load(scope), updated)
})

test('invalid scopes, quantities, sizes and revisions cannot create drafts', async () => {
  const store = createBasketStore(new IDBFactory())
  for (const value of [{ ...input(), serviceOrigin: 'http://branch.example.test' }, { ...input(), employeeId: '' }, { ...input(), sessionMasterDataVersion: '' }, { ...input(), lines: [{ ...line, quantity: 0 }] }, { ...input(), lines: [{ ...line, comment: 'a'.repeat(65537) }] }]) await assert.rejects(store.save(value, null))
  await assert.rejects(store.save(input(), 1))
  assert.deepEqual(await store.list(identity), [])
})

test('staging atomically freezes basket and journals original bytes across reload', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null)
  const pending = await store.stage(scope, draft, h.snapshot)
  assert.equal(pending.pendingRequestId, 'REQUEST-01')
  assert.deepEqual(await createBasketStore(factory).load(scope), pending)
  assert.deepEqual((await journal.load(identity, 'REQUEST-01'))?.command, h.snapshot)
  assert.equal(h.calls.length, 0)
  await assert.rejects(store.save(input(), pending))
  await assert.rejects(store.remove(scope, pending))
  await journal.resolve(h.snapshot)
  await assert.rejects(journal.finalize(h.snapshot))
  assert.ok(await journal.load(identity, 'REQUEST-01'))
})

test('mismatched snapshots and actions cannot bind a saved basket', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory)
  const draft = await store.save(input(), null), h = client()
  const body = JSON.parse(h.snapshot.body)
  const wrongLines = { ...h.snapshot, body: JSON.stringify({ ...body, lines: [{ ...body.lines[0], fulfillmentType: 'DineIn' }] }) }
  const openClient = createOrderingClient({ ...identity, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' })
  const openSnapshot = openClient.snapshot(openClient.prepareOpenTable('A01', 'SESSION-01'))
  for (const snapshot of [wrongLines, client([], 'REQUEST-02', { employeeId: 'OTHER' }).snapshot, openSnapshot]) await assert.rejects(store.stage(scope, draft, snapshot))
  assert.deepEqual(await journal.list(identity), [])
  assert.deepEqual(await store.load(scope), draft)
})

test('an existing request ID cannot be attached to a new basket even with identical bytes', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null)
  await journal.add(h.snapshot)
  await assert.rejects(store.stage(scope, draft, h.snapshot))
  assert.deepEqual(await store.load(scope), draft)
  assert.equal((await journal.list(identity)).length, 1)
})

test('lost response recovers with original request and atomically clears accepted basket plus journal', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), h = client([json(receipt('NoDurableResult'), 404), new Error('Connection lost')])
  const draft = await store.save(input(), null), pending = await store.stage(scope, draft, h.snapshot)
  const recovery = createCommandRecovery(h.api, createCommandJournal(factory))
  assert.equal((await recovery.retry('REQUEST-01')).result.kind, 'uncertain')
  assert.equal(h.calls[1].body, h.snapshot.body)
  const next = client([json(receipt())]), journal = createCommandJournal(factory)
  const checked = await createCommandRecovery(next.api, journal).recover('REQUEST-01')
  assert.equal(checked.result.kind, 'durable')
  if (checked.result.kind !== 'durable') throw new Error('Expected durable receipt')
  assert.deepEqual(await createBasketStore(factory).apply(scope, pending, checked.result.response), { kind: 'accepted', basket: null })
  assert.equal(await store.load(scope), null)
  assert.equal(await journal.load(identity, 'REQUEST-01'), null)
  assert.equal(next.calls.length, 1)
  assert.equal(next.calls[0].method, 'GET')
})

test('rejection retains the original basket for review and atomically removes its old command', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null), pending = await store.stage(scope, draft, h.snapshot)
  await journal.resolve(h.snapshot)
  const applied = await store.apply(scope, pending, receipt('Rejected'))
  assert.equal(applied.kind, 'rejected')
  assert.deepEqual(applied.basket?.lines, draft.lines)
  assert.equal(applied.basket?.pendingRequestId, null)
  assert.equal(applied.basket?.revision, pending.revision + 1)
  assert.equal(await journal.load(identity, 'REQUEST-01'), null)
  await assert.rejects(store.apply(scope, pending, receipt('Rejected')))
  const revised = client([], 'REQUEST-02')
  assert.equal((await store.stage(scope, applied.basket!, revised.snapshot)).pendingRequestId, 'REQUEST-02')
})

test('unresolved or mismatched receipts never clear basket or recovery evidence', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null), pending = await store.stage(scope, draft, h.snapshot)
  await assert.rejects(store.apply(scope, pending, receipt()))
  await journal.resolve(h.snapshot)
  for (const response of [receipt('NoDurableResult'), { ...receipt(), result: { ...receipt().result, requestId: 'OTHER' } }, { ...receipt(), result: { ...receipt().result, tableSessionId: 'OTHER' } }, { ...receipt(), result: { ...receipt().result, schemaVersion: 2 } }]) await assert.rejects(store.apply(scope, pending, response))
  assert.deepEqual(await store.load(scope), pending)
  assert.equal((await journal.load(identity, 'REQUEST-01'))?.state, 'resolved')
})

test('schema-1 migration preserves pending commands byte-for-byte', async () => {
  const factory = new IDBFactory(), h = client()
  const old = await new Promise<IDBDatabase>((resolve, reject) => {
    const request = factory.open(orderingDatabaseName, 1)
    request.onupgradeneeded = () => { const store = request.result.createObjectStore(commandsStore, { keyPath: ['command.serviceOrigin', 'command.branchId', 'command.deviceId', 'command.requestId'] }); store.createIndex('scope', ['command.serviceOrigin', 'command.branchId', 'command.deviceId']) }
    request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error)
  })
  await write(old, commandsStore, { command: h.snapshot, state: 'pending' }); old.close()
  const store = createBasketStore(factory), journal = createCommandJournal(factory)
  assert.deepEqual(await store.list(identity), [])
  assert.equal((await journal.load(identity, 'REQUEST-01'))?.command.body, h.snapshot.body)
  await store.save(input(), null)
  assert.equal((await journal.load(identity, 'REQUEST-01'))?.state, 'pending')
})

test('a blocked schema upgrade fails closed and retries after the old connection closes', async () => {
  const factory = new IDBFactory(), h = client()
  const old = await new Promise<IDBDatabase>((resolve, reject) => {
    const request = factory.open(orderingDatabaseName, 1)
    request.onupgradeneeded = () => { const store = request.result.createObjectStore(commandsStore, { keyPath: ['command.serviceOrigin', 'command.branchId', 'command.deviceId', 'command.requestId'] }); store.createIndex('scope', ['command.serviceOrigin', 'command.branchId', 'command.deviceId']) }
    request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error)
  })
  await write(old, commandsStore, { command: h.snapshot, state: 'pending' })
  const baskets = createBasketStore(factory)
  await assert.rejects(baskets.list(identity), /could not be opened/)
  old.close()
  assert.deepEqual(await baskets.list(identity), [])
  assert.equal((await createCommandJournal(factory).load(identity, 'REQUEST-01'))?.command.body, h.snapshot.body)
})

test('constraint failure after journal insertion rolls back both staging writes', async () => {
  const factory = new IDBFactory(), store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null)
  const db = await open(factory)
  // Isolated corruption creates an index conflict, forcing abort after journal.add succeeds.
  await write(db, basketsStore, { ...draft, tableSessionId: 'OTHER-SESSION', pendingRequestId: 'REQUEST-01' }); db.close()
  await assert.rejects(store.stage(scope, draft, h.snapshot))
  assert.deepEqual(await store.load(scope), draft)
  assert.equal(await journal.load(identity, 'REQUEST-01'), null)
})

test('abort after journal deletion rolls back accepted basket cleanup', async () => {
  const factory = new IDBFactory(), originalOpen = factory.open.bind(factory)
  let abortDeletion = false
  factory.open = (...args) => {
    const request = originalOpen(...args)
    request.addEventListener('success', () => {
      const db = request.result, originalTransaction = db.transaction.bind(db)
      db.transaction = (...txArgs) => {
        const tx = originalTransaction(...txArgs), originalStore = tx.objectStore.bind(tx)
        tx.objectStore = name => {
          const store = originalStore(name)
          if (name === commandsStore && abortDeletion) {
            const originalDelete = store.delete.bind(store)
            store.delete = query => { const deletion = originalDelete(query); deletion.addEventListener('success', () => tx.abort()); return deletion }
          }
          return store
        }
        return tx
      }
    })
    return request
  }
  const store = createBasketStore(factory), journal = createCommandJournal(factory), h = client()
  const draft = await store.save(input(), null), pending = await store.stage(scope, draft, h.snapshot)
  await journal.resolve(h.snapshot); abortDeletion = true
  await assert.rejects(store.apply(scope, pending, receipt()))
  assert.deepEqual(await createBasketStore(factory).load(scope), pending)
  assert.equal((await journal.load(identity, 'REQUEST-01'))?.state, 'resolved')
})
