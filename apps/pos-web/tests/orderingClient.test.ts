import test from 'node:test'
import assert from 'node:assert/strict'
import { createOrderingClient } from '../src/api/orderingClient.ts'

const session = {
  serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01',
  deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb',
}
const line = { sellableItemId: 'ITEM-01', quantity: 1, fulfillmentType: 'DineIn' as const, comment: null, options: [] }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })
function receipt(status = 'BranchAccepted', requestId = 'REQUEST-01', tableSessionId = 'SESSION-01') {
  return {
    result: {
      schemaVersion: 1, requestId, status, tableSessionId, tableSessionStatus: 'Ordering', tableVersion: 1,
      errorCode: status === 'Rejected' ? 'CatalogValidationFailed' : null, serverReceivedAt: '2026-10-31T05:00:00.0000000+00:00', branchSequence: 2,
    },
    validationDetails: status === 'Rejected' ? { schemaVersion: 1, errors: [{ lineSequence: 1, code: 'itemSoldOut', sellableItemId: 'ITEM-01', optionGroupId: null }] } : null,
  }
}
function harness(responses: (Response | Error)[]) {
  const calls: { url: string; init: RequestInit }[] = []
  const client = createOrderingClient(session, {
    requestId: () => 'REQUEST-01', now: () => new Date('2026-10-31T05:00:00Z'),
    fetch: async (url, init) => {
      calls.push({ url, init })
      const response = responses.shift()
      if (response instanceof Error) throw response
      assert.ok(response, 'Unexpected automatic request')
      return response
    },
  })
  return { client, calls }
}

test('open uses fixed HTTPS origin, exact identity and safe credential headers', async () => {
  const { client, calls } = harness([json(receipt('BranchAccepted', 'REQUEST-01', 'EXISTING-SESSION'))])
  const command = client.prepareOpenTable('A01', 'PROPOSED-SESSION')
  assert.equal((await client.execute(command)).kind, 'durable')
  assert.equal(calls[0].url, 'https://branch.example.test/api/v1/ordering/tables/open')
  assert.equal(calls[0].init.credentials, 'omit')
  assert.equal(calls[0].init.cache, 'no-store')
  assert.equal(calls[0].init.redirect, 'error')
  const headers = calls[0].init.headers as Record<string, string>
  assert.equal(headers.Authorization, `Device ${session.deviceCredential}`)
  assert.equal(headers['X-POS-Employee-Session'], session.employeeSessionToken)
  const body = JSON.parse(calls[0].init.body as string)
  assert.deepEqual(body.context, { requestId: 'REQUEST-01', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01', tableSessionId: 'PROPOSED-SESSION', clientCreatedAt: '2026-10-31T05:00:00.000Z' })
  assert.ok(!JSON.stringify(command).includes(session.deviceCredential))
  assert.ok(!JSON.stringify(client).includes(session.employeeSessionToken))
})

test('submit snapshots basket and never sends prices or a caller batch ID', async () => {
  const { client, calls } = harness([json(receipt()), json(receipt())])
  const basket = [{ ...line, quantity: 2, unitPrice: 1, options: [{ optionGroupId: 'GROUP-1', sellableItemId: 'OPTION-1', quantity: 1 }] }]
  const command = client.prepareSubmitOrder('SESSION-01', basket)
  basket[0].quantity = 9; basket[0].options[0].quantity = 4
  await client.execute(command); await client.execute(command)
  assert.equal(calls[0].init.body, calls[1].init.body)
  const body = JSON.parse(calls[0].init.body as string)
  assert.equal(body.lines[0].quantity, 2)
  assert.equal(body.lines[0].options[0].quantity, 1)
  assert.ok(!('unitPrice' in body.lines[0]))
  assert.ok(!('orderBatchId' in body))
})

test('durable 409 retains machine-readable line rejection details', async () => {
  const body = receipt('Rejected')
  const { client } = harness([json(body, 409)])
  assert.deepEqual(await client.execute(client.prepareSubmitOrder('SESSION-01', [line])), { kind: 'durable', response: body })
})

for (const status of [400, 401, 403, 413]) {
  test(`HTTP ${status} is blocked without a fabricated durable rejection or retry`, async () => {
    const { client, calls } = harness([new Response(null, { status })])
    assert.deepEqual(await client.execute(client.prepareSubmitOrder('SESSION-01', [line])), { kind: 'blocked', requestId: 'REQUEST-01', httpStatus: status })
    assert.equal(calls.length, 1)
  })
}

test('network failure remains uncertain and command lookup preserves the same request ID', async () => {
  const { client, calls } = harness([new TypeError('synthetic network failure'), json(receipt())])
  const command = client.prepareSubmitOrder('SESSION-01', [line])
  assert.deepEqual(await client.execute(command), { kind: 'uncertain', requestId: 'REQUEST-01', reason: 'network' })
  assert.equal(calls.length, 1)
  assert.equal((await client.commandStatus(command.requestId)).kind, 'durable')
  assert.equal(calls[1].init.method, 'GET')
  assert.ok(calls[1].url.endsWith('/commands/REQUEST-01'))
})

test('503 is uncertain and never triggers an automatic mutation retry', async () => {
  const { client, calls } = harness([new Response(null, { status: 503 })])
  assert.deepEqual(await client.execute(client.prepareSubmitOrder('SESSION-01', [line])), { kind: 'uncertain', requestId: 'REQUEST-01', reason: 'http', httpStatus: 503 })
  assert.equal(calls.length, 1)
})

test('404 NoDurableResult does not become a rejected or accepted command', async () => {
  const unknown = receipt('NoDurableResult')
  for (const key of ['tableSessionId', 'tableSessionStatus', 'tableVersion', 'errorCode', 'serverReceivedAt', 'branchSequence']) (unknown.result as Record<string, unknown>)[key] = null
  unknown.validationDetails = null
  const { client } = harness([json(unknown, 404)])
  assert.deepEqual(await client.commandStatus('REQUEST-01'), { kind: 'noDurableResult', requestId: 'REQUEST-01' })
})

for (const change of ['requestId', 'tableSessionId', 'schemaVersion', 'status', 'tableVersion', 'serverReceivedAt']) {
  test(`mismatched or malformed ${change} cannot confirm an order`, async () => {
    const invalid = receipt()
    const values: Record<string, unknown> = { requestId: 'OTHER', tableSessionId: 'OTHER', schemaVersion: 2, status: 'NoDurableResult', tableVersion: -1, serverReceivedAt: 'invalid' }
    ;(invalid.result as Record<string, unknown>)[change] = values[change]
    const { client } = harness([json(invalid)])
    assert.deepEqual(await client.execute(client.prepareSubmitOrder('SESSION-01', [line])), { kind: 'uncertain', requestId: 'REQUEST-01', reason: 'protocol' })
  })
}

test('timeout and cancellation retain command identity without retry', async () => {
  let count = 0
  const fetch = async (_url: string, init: RequestInit): Promise<Response> => {
    count++
    return new Promise((_resolve, reject) => init.signal!.addEventListener('abort', () => reject(new DOMException('Aborted', 'AbortError')), { once: true }))
  }
  const client = createOrderingClient(session, { fetch, timeoutMs: 10, requestId: () => 'REQUEST-01' })
  const command = client.prepareSubmitOrder('SESSION-01', [line])
  assert.deepEqual(await client.execute(command), { kind: 'uncertain', requestId: 'REQUEST-01', reason: 'timeout' })
  const cancellation = new AbortController(); cancellation.abort()
  assert.deepEqual(await client.execute(command, cancellation.signal), { kind: 'uncertain', requestId: 'REQUEST-01', reason: 'aborted' })
  assert.equal(count, 1)
})

test('non-JSON, corrupt UTF-8, and oversized response streams cannot confirm commands', async () => {
  const bodies = [
    new Response('<html>proxy error</html>'),
    new Response('not JSON', { headers: { 'Content-Type': 'application/json' } }),
    new Response(new Uint8Array([0xff]), { headers: { 'Content-Type': 'application/json' } }),
    new Response(new Uint8Array(100), { headers: { 'Content-Type': 'application/json' } }),
  ]
  for (const response of bodies) {
    const client = createOrderingClient(session, { requestId: () => 'REQUEST-01', maxResponseBytes: 64, fetch: async () => response })
    assert.equal((await client.execute(client.prepareSubmitOrder('SESSION-01', [line]))).kind, 'uncertain')
  }
})

test('active list preserves partial-floor-plan flag and refuses duplicate sessions', async () => {
  const active = { schemaVersion: 1, includesAvailableTables: false, sessions: [{ tableSessionId: 'SESSION-01', tableId: 'A01', status: 'Ordering', tableVersion: 1 }] }
  const { client } = harness([json(active), json({ ...active, sessions: [...active.sessions, ...active.sessions] })])
  assert.deepEqual(await client.activeTables(), { kind: 'ok', value: active })
  assert.deepEqual(await client.activeTables(), { kind: 'unavailable', reason: 'protocol' })
})

test('menu preserves frozen prices and current availability, with no client repricing', async () => {
  const menu = {
    schemaVersion: 1, tableSessionId: 'SESSION-01', masterDataVersion: 'MASTER-OLD', items: [{
      sellableItemId: 'ITEM-01', kind: 'Item', displayName: 'Test item', unitPrice: 100,
      availability: { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: true }, optionGroups: [],
    }],
  }
  const { client } = harness([json(menu), json({ ...menu, tableSessionId: 'OTHER' }), json({ schemaVersion: 1, errorCode: 'ActiveSessionMenuNotFound' }, 404)])
  assert.deepEqual(await client.sessionMenu('SESSION-01'), { kind: 'ok', value: menu })
  assert.deepEqual(await client.sessionMenu('SESSION-01'), { kind: 'unavailable', reason: 'protocol' })
  assert.deepEqual(await client.sessionMenu('SESSION-01'), { kind: 'notFound' })
})

test('insecure and credential-bearing origins are rejected before any request', () => {
  for (const serviceOrigin of ['http://branch.example.test', 'https://user:password@branch.example.test', 'https://branch.example.test/path', 'https://branch.example.test?query=1']) {
    assert.throws(() => createOrderingClient({ ...session, serviceOrigin }))
  }
})

test('menu retains SubItem and Topping group constraints and rejects contradictory limits', async () => {
  const availability = { existsInActivePackage: true, centralActive: true, branchEnabled: true, hasCurrentPrice: true, soldOut: false }
  const optionGroups = ['SubItem', 'Topping'].map(kind => ({
    optionGroupId: `GROUP-${kind}`, displayName: kind, optionType: kind, minSelections: 1, maxSelections: 1,
    allowRepeatedOption: false, maxQuantityPerOption: 1,
    options: [{ sellableItemId: `OPTION-${kind}`, kind, displayName: kind, unitPrice: 10, availability, optionGroups: [] }],
  }))
  const menu = { schemaVersion: 1, tableSessionId: 'SESSION-01', masterDataVersion: 'MASTER-OLD', items: [{ sellableItemId: 'ITEM-01', kind: 'Item', displayName: 'Test', unitPrice: 100, availability, optionGroups }] }
  const valid = json(menu)
  optionGroups[0].maxSelections = 0
  const { client } = harness([valid, json(menu)])
  const result = await client.sessionMenu('SESSION-01')
  assert.equal(result.kind, 'ok')
  if (result.kind === 'ok') {
    assert.equal(result.value.items[0].optionGroups[1].optionType, 'Topping')
    assert.equal(result.value.items[0].optionGroups[0].options[0].unitPrice, 10)
  }
  assert.deepEqual(await client.sessionMenu('SESSION-01'), { kind: 'unavailable', reason: 'protocol' })
})

test('prepared commands cannot cross employee sessions or reuse generated IDs', async () => {
  const first = harness([]).client, second = harness([]).client
  const command = first.prepareSubmitOrder('SESSION-01', [line])
  await assert.rejects(second.execute(command), /another ordering session/)
  assert.throws(() => first.prepareOpenTable('A02', 'SESSION-02'), /new request identifier/)
})

test('input validation rejects invalid quantities and oversized baskets before sending', () => {
  for (const quantity of [0, -1, 0.5, Infinity, NaN]) assert.throws(() => harness([]).client.prepareSubmitOrder('SESSION-01', [{ ...line, quantity }]))
  assert.throws(() => harness([]).client.prepareSubmitOrder('SESSION-01', [{ ...line, comment: 'x'.repeat(65536) }]), /64 KiB/)
  assert.throws(() => harness([]).client.prepareOpenTable('A01', '..'))
})

function sessionDetailFixture() {
  return {
    schemaVersion: 1, session: { tableSessionId: 'SESSION-01', tableId: 'A01', status: 'Ordering', tableVersion: 1 },
    asOf: '2026-10-31T05:00:00Z', hasOpenCancellationRequest: false,
    billPreviewTotals: {
      masterDataVersion: 'MASTER-OLD', calculationPolicyVersion: 'CALC-1', vatRate: 7, serviceChargeRate: 0,
      itemGrossAmount: 110, lineDiscountAmount: 0, itemNetAmount: 110, billDiscountAmount: 0,
      serviceChargeBase: 110, serviceChargeAmount: 0, totalBeforeRounding: 110,
      roundingAdjustment: 0, netTotal: 110, vatInclusiveAmount: 7.2, amountBeforeVat: 102.8,
    },
    batches: [{
      orderBatchId: 'BRANCH-01:REQUEST-01:batch:1', requestId: 'REQUEST-01', branchSequence: 2,
      serverReceivedAt: '2026-10-31T05:00:00Z', acceptanceStatus: 'BranchAccepted',
      activeGrossAmount: 110, activeLineDiscountAmount: 0, activeItemNetAmount: 110,
      lines: [{
        orderLineId: 'BRANCH-01:REQUEST-01:line:1', lineSequence: 1, sellableItemId: 'ITEM-01', displayName: 'Test item',
        originalQuantity: 1, activeQuantity: 1, status: 'Active', unitPrice: 100, fulfillmentType: 'Takeaway', comment: null,
        options: [{ sellableItemId: 'OPTION-1', optionGroupId: 'GROUP-1', displayName: 'Test option', quantityPerItem: 1, unitPrice: 10 }],
        activeGrossAmount: 110, activeDiscountAmount: 0, activeNetAmount: 110,
      }],
    }],
  }
}

test('session detail preserves authoritative batches, fulfillment, options and amounts unchanged', async () => {
  const body = sessionDetailFixture()
  const { client, calls } = harness([json(body)])
  assert.deepEqual(await client.sessionDetail('SESSION-01'), { kind: 'ok', value: body })
  assert.ok(calls[0].url.endsWith('/table-sessions/SESSION-01'))
  assert.equal(calls[0].init.method, 'GET')
})

test('empty and fully cancelled session detail have no active bill; missing session stays not found', async () => {
  const empty = { ...sessionDetailFixture(), batches: [], billPreviewTotals: null }
  const cancelled = sessionDetailFixture()
  cancelled.batches[0].lines[0].activeQuantity = 0
  cancelled.batches[0].lines[0].status = 'Cancelled'
  cancelled.batches[0].lines[0].activeGrossAmount = 0
  cancelled.batches[0].lines[0].activeNetAmount = 0
  cancelled.batches[0].activeGrossAmount = 0
  cancelled.batches[0].activeItemNetAmount = 0
  const noActiveBill = { ...cancelled, billPreviewTotals: null }
  const { client } = harness([json(empty), json(noActiveBill), json({ schemaVersion: 1, errorCode: 'ActiveTableSessionNotFound' }, 404)])
  assert.deepEqual(await client.sessionDetail('SESSION-01'), { kind: 'ok', value: empty })
  assert.deepEqual(await client.sessionDetail('SESSION-01'), { kind: 'ok', value: noActiveBill })
  assert.deepEqual(await client.sessionDetail('SESSION-01'), { kind: 'notFound' })
})

for (const fault of ['session', 'receipt', 'duplicateLine', 'missingTotals', 'negativeTotal', 'quantity', 'readTime']) {
  test(`invalid session-detail ${fault} does not become authoritative UI data`, async () => {
    const body = sessionDetailFixture()
    switch (fault) {
      case 'session': body.session.tableSessionId = 'OTHER'; break
      case 'receipt': body.batches[0].acceptanceStatus = 'Rejected'; break
      case 'duplicateLine': body.batches[0].lines.push(body.batches[0].lines[0]); break
      case 'missingTotals': (body as Record<string, unknown>).billPreviewTotals = null; break
      case 'negativeTotal': body.billPreviewTotals.netTotal = -1; break
      case 'quantity': body.batches[0].lines[0].activeQuantity = 2; break
      case 'readTime': body.asOf = 'invalid'; break
    }
    const { client } = harness([json(body)])
    assert.deepEqual(await client.sessionDetail('SESSION-01'), { kind: 'unavailable', reason: 'protocol' })
  })
}
