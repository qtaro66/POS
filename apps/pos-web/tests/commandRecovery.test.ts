import test from 'node:test'
import assert from 'node:assert/strict'
import { IDBFactory } from 'fake-indexeddb'
import { createOrderingClient, validateCommandSnapshot } from '../src/api/orderingClient.ts'
import { createCommandJournal } from '../src/ordering/commandJournal.ts'
import { createCommandRecovery } from '../src/ordering/commandRecovery.ts'

const session = { serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01', deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }
const line = { sellableItemId: 'ITEM-01', quantity: 2, fulfillmentType: 'Takeaway' as const, comment: 'ไม่เผ็ด', options: [{ optionGroupId: 'TOPPING', sellableItemId: 'EGG', quantity: 1 }] }
function response(status = 'BranchAccepted', tableSessionId: string | null = 'SESSION-01') {
  return {
    result: { schemaVersion: 1, requestId: 'REQUEST-01', status, tableSessionId: status === 'NoDurableResult' ? null : tableSessionId, tableSessionStatus: status === 'NoDurableResult' ? null : 'Ordering', tableVersion: status === 'NoDurableResult' ? null : 1, errorCode: status === 'Rejected' ? 'SoldOut' : null, serverReceivedAt: status === 'NoDurableResult' ? null : '2026-10-31T05:00:00Z', branchSequence: status === 'NoDurableResult' ? null : 1 },
    validationDetails: null,
  }
}
function harness(replies: (Response | Error)[] = [], overrides = {}) {
  const calls: { url: string; init: RequestInit }[] = []
  const client = createOrderingClient({ ...session, ...overrides }, {
    requestId: () => 'REQUEST-01', now: () => new Date('2026-10-31T05:00:00Z'),
    fetch: async (url, init) => {
      calls.push({ url, init })
      const reply = replies.shift()
      if (reply instanceof Error) throw reply
      assert.ok(reply, 'Unexpected network operation')
      return reply
    },
  })
  return { client, calls }
}
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
const missing = () => json(response('NoDurableResult'), 404)
const command = (client: ReturnType<typeof createOrderingClient>) => client.prepareSubmitOrder('SESSION-01', [line])

test('reload restores original bytes and credentials never enter the journal', async () => {
  const factory = new IDBFactory(), store = createCommandJournal(factory)
  const first = harness([new Error('Connection lost')])
  const recovery = createCommandRecovery(first.client, store)
  assert.equal((await recovery.send(command(first.client))).result.kind, 'uncertain')
  const second = harness([missing(), json(response())])
  const reloaded = createCommandRecovery(second.client, createCommandJournal(factory))
  const entries = await reloaded.list()
  assert.equal(entries.length, 1)
  assert.equal(entries[0].state, 'pending')
  const stored = JSON.stringify(entries)
  assert.ok(!stored.includes(session.deviceCredential) && !stored.includes(session.employeeSessionToken))
  assert.equal(second.calls.length, 0)
  assert.equal((await reloaded.retry('REQUEST-01')).result.kind, 'durable')
  assert.equal(second.calls[0].init.method, 'GET')
  assert.equal(second.calls[1].init.body, first.calls[0].init.body)
  assert.equal((await reloaded.list())[0].state, 'resolved')
  await reloaded.finalize('REQUEST-01')
  assert.deepEqual(await reloaded.list(), [])
})

test('recovery performs GET only and NoDurableResult retains the pending entry', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness([missing()])
  await store.add(h.client.snapshot(command(h.client)))
  const recovery = createCommandRecovery(h.client, store)
  assert.equal((await recovery.recover('REQUEST-01')).result.kind, 'noDurableResult')
  assert.equal(h.calls.length, 1)
  assert.equal(h.calls[0].init.method, 'GET')
  assert.equal((await recovery.list())[0].state, 'pending')
  await assert.rejects(recovery.finalize('REQUEST-01'))
})

for (const status of ['BranchAccepted', 'Rejected']) {
  test(`explicit retry reconciles ${status} without sending another POST`, async () => {
    const store = createCommandJournal(new IDBFactory()), h = harness([json(response(status))])
    await store.add(h.client.snapshot(command(h.client)))
    const recovery = createCommandRecovery(h.client, store)
    assert.equal((await recovery.retry('REQUEST-01')).result.kind, 'durable')
    assert.equal(h.calls.length, 1)
    assert.equal(h.calls[0].init.method, 'GET')
    assert.equal((await recovery.list())[0].state, 'resolved')
  })
}

test('unavailable lookup prevents retry POST', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness([new Response(null, { status: 503 })])
  await store.add(h.client.snapshot(command(h.client)))
  assert.equal((await createCommandRecovery(h.client, store).retry('REQUEST-01')).result.kind, 'unavailable')
  assert.equal(h.calls.length, 1)
  assert.equal((await store.list(h.client.scope))[0].state, 'pending')
})

test('missing storage and transaction abort fail closed before POST', async () => {
  for (const store of [createCommandJournal(undefined), {
    ...createCommandJournal(new IDBFactory()), add: async () => { throw new Error('Quota exceeded') },
  }]) {
    const h = harness()
    await assert.rejects(createCommandRecovery(h.client, store).send(command(h.client)))
    assert.equal(h.calls.length, 0)
  }
})

test('accepted Branch result survives local resolve failure and remains recoverable', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness([json(response()), json(response())])
  const failing = { ...store, resolve: async () => { throw new Error('Disk full') } }
  const outcome = await createCommandRecovery(h.client, failing).send(command(h.client))
  assert.equal(outcome.result.kind, 'durable')
  assert.equal(outcome.recoveryRecorded, false)
  assert.equal((await store.list(h.client.scope))[0].state, 'pending')
  assert.equal((await createCommandRecovery(h.client, store).recover('REQUEST-01')).result.kind, 'durable')
  assert.equal(h.calls[1].init.method, 'GET')
})

test('atomic journal add never overwrites a reused ID with different payload', async () => {
  const factory = new IDBFactory(), store = createCommandJournal(factory), h = harness()
  const snapshot = h.client.snapshot(command(h.client))
  assert.deepEqual((await Promise.all([store.add(snapshot), createCommandJournal(factory).add(snapshot)])).sort(), [false, true])
  const payload = JSON.parse(snapshot.body)
  payload.lines[0].quantity = 3
  await assert.rejects(store.add({ ...snapshot, body: JSON.stringify(payload) }))
  assert.equal((await store.load(h.client.scope, snapshot.requestId))?.command.body, snapshot.body)
})

test('scopes isolate service origins, branches and devices; employees can query but cannot resend another identity', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness()
  await store.add(h.client.snapshot(command(h.client)))
  for (const scope of [{ ...h.client.scope, serviceOrigin: 'https://other.example.test' }, { ...h.client.scope, branchId: 'OTHER' }, { ...h.client.scope, deviceId: 'OTHER' }]) {
    assert.deepEqual(await store.list(scope), [])
    assert.equal(await store.load(scope, 'REQUEST-01'), null)
  }
  const other = harness([missing()], { employeeId: 'EMP-02' })
  await assert.rejects(createCommandRecovery(other.client, store).retry('REQUEST-01'), /original ordering identity/)
  assert.equal(other.calls.length, 1)
  assert.equal(other.calls[0].init.method, 'GET')
})

test('submit recovery rejects a receipt for a different session; OpenTable permits existing session', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness([json(response('BranchAccepted', 'OTHER-SESSION'))])
  await store.add(h.client.snapshot(command(h.client)))
  assert.equal((await createCommandRecovery(h.client, store).recover('REQUEST-01')).result.kind, 'unavailable')
  const openStore = createCommandJournal(new IDBFactory()), open = harness([new Error('Lost'), json(response('BranchAccepted', 'EXISTING-SESSION'))])
  const openRecovery = createCommandRecovery(open.client, openStore)
  await openRecovery.send(open.client.prepareOpenTable('A01', 'PROPOSED-SESSION'))
  assert.equal((await openRecovery.recover('REQUEST-01')).result.kind, 'durable')
})

test('canonical snapshots reject malformed, extra-field, modified-context and oversized bodies', () => {
  const h = harness(), snapshot = h.client.snapshot(command(h.client))
  const payload = JSON.parse(snapshot.body)
  for (const value of [
    { ...snapshot, formatVersion: 2 }, { ...snapshot, credential: 'forbidden' },
    { ...snapshot, body: snapshot.body + ' ' }, { ...snapshot, body: '{broken' },
    { ...snapshot, body: JSON.stringify({ ...payload, unitPrice: 5 }) },
    { ...snapshot, requestId: 'DIFFERENT' }, { ...snapshot, employeeId: 'DIFFERENT' },
    { ...snapshot, serviceOrigin: 'http://branch.example.test' },
    { ...snapshot, body: JSON.stringify({ ...payload, lines: [{ ...payload.lines[0], quantity: -1 }] }) },
    { ...snapshot, body: 'a'.repeat(65537) },
  ]) assert.throws(() => validateCommandSnapshot(value))
  assert.deepEqual(validateCommandSnapshot(JSON.parse(JSON.stringify(snapshot))), snapshot)
})

test('restore reserves the original ID and rejects collisions within the client', () => {
  const first = harness(), second = harness()
  const snapshot = first.client.snapshot(command(first.client))
  second.client.restore(snapshot)
  assert.throws(() => command(second.client), /new request identifier/)
  const body = JSON.parse(snapshot.body); body.lines[0].quantity = 7
  assert.throws(() => second.client.restore({ ...snapshot, body: JSON.stringify(body) }), /another payload/)
})

test('existing journal entries cannot be submitted through send again', async () => {
  const h = harness([new Error('Lost')]), recovery = createCommandRecovery(h.client, createCommandJournal(new IDBFactory()))
  const prepared = command(h.client)
  await recovery.send(prepared)
  await assert.rejects(recovery.send(prepared), /already journaled/)
  assert.equal(h.calls.length, 1)
})

test('real IndexedDB collision abort prevents a send from replacing an existing command', async () => {
  const store = createCommandJournal(new IDBFactory()), first = harness(), next = harness()
  await store.add(first.client.snapshot(command(first.client)))
  const changed = next.client.prepareSubmitOrder('SESSION-01', [{ ...line, quantity: 9 }])
  await assert.rejects(createCommandRecovery(next.client, store).send(changed))
  assert.equal(next.calls.length, 0)
  assert.equal(JSON.parse((await store.list(first.client.scope))[0].command.body).lines[0].quantity, 2)
})

test('a previously resolved command is never resent if Branch later reports no durable result', async () => {
  const store = createCommandJournal(new IDBFactory()), h = harness([json(response()), missing()])
  const recovery = createCommandRecovery(h.client, store)
  await recovery.send(command(h.client))
  assert.equal((await recovery.retry('REQUEST-01')).result.kind, 'noDurableResult')
  assert.equal(h.calls.length, 2)
  assert.equal(h.calls[1].init.method, 'GET')
})

test('resolved entries remain visible across reload and are freshly checked before retry', async () => {
  const factory = new IDBFactory(), h = harness([json(response())])
  await createCommandRecovery(h.client, createCommandJournal(factory)).send(command(h.client))
  const next = harness([new Response(null, { status: 503 })]), recovery = createCommandRecovery(next.client, createCommandJournal(factory))
  assert.equal((await recovery.list())[0].state, 'resolved')
  assert.equal((await recovery.retry('REQUEST-01')).result.kind, 'unavailable')
  assert.equal(next.calls.length, 1)
})
