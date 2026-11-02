import test from 'node:test'
import assert from 'node:assert/strict'
import { createOrderingClient } from '../src/api/orderingClient.ts'
import { parsePrintAttentionPage } from '../src/api/printAttentionContracts.ts'
import { parseCloudOutboxPage } from '../src/api/cloudOutboxContracts.ts'
import { parseCloudReplayPage } from '../src/api/cloudReplayContracts.ts'
import { parsePrinterInventoryPage } from '../src/api/printerInventoryContracts.ts'

const attentionItem = { printJobId: 'PRINT-1', requestId: 'REQUEST-1', tableSessionId: 'SESSION-1', snapshotId: 'SNAPSHOT-1',
  purpose: 'KitchenOrder', targetStation: 'KITCHEN-1', status: 'Unknown', createdAt: '2026-11-02T03:00:00Z', assignedPrinterId: null,
  nextRetryAt: null, attentionReason: 'SendOutcomeUnknown', latestAttemptStatus: 'Unknown', latestAttemptFinishedAt: null }
const attentionPage = { schemaVersion: 1, observedAt: '2026-11-02T04:00:00Z', totalCount: 2, items: [attentionItem], nextCursor: 'NEXT-CURSOR' }
const cloudItem = { outboxId: 'EVENT-1', branchSequence: '9007199254740993', requestId: 'REQUEST-1', tableSessionId: 'SESSION-1', action: 'SubmitOrder',
  processingStatus: 'Failed', createdAt: '2026-11-02T03:00:00Z', retryCount: 2, nextRetryAt: null, leaseExpiresAt: null, lastFailedAt: null, expectedBranchSequence: '9007199254740992' }
const cloudPage = { schemaVersion: 1, observedAt: '2026-11-02T04:00:00Z', summary: { unacknowledgedCount: 1, pendingCount: 0, processingCount: 0, failedCount: 1, otherCount: 0 }, items: [cloudItem], nextAfterSequence: null }
const replayItem = { recoveryId: 'RECOVERY-1', outboxId: 'EVENT-1', expectedBranchSequence: '9007199254740993', receivedBranchSequence: '9223372036854775807',
  requestId: 'REQUEST-1', tableSessionId: 'SESSION-1', action: 'SubmitOrder', requestedAt: '2026-11-02T03:00:00Z', hasLease: false,
  retryCount: 2, nextRetryAt: null, leaseExpiresAt: null, lastFailedAt: null, lastExpectedBranchSequence: '9007199254740992' }
const replayPage = { schemaVersion: 1, observedAt: '2026-11-02T04:00:00Z', summary: { pendingCount: 1, leasedCount: 0, unleasedCount: 1, withRetriesCount: 1 }, items: [replayItem], nextAfterSequence: null }
const printerItem = { printerId: 'PRINTER-01', displayName: 'Example printer', productionStationId: 'CASHIER', host: 'printer.branch.test', port: 9100,
  protocol: 'RawTcpEscPos', compatibilityProfileId: 'TEST-PROFILE', manufacturer: 'Example', model: 'Thermal Example', productCode: null, firmwareVersion: null,
  paperWidthMillimeters: 80, statusMode: 'Unsupported', supportsCashDrawer: true, isActive: true, updatedAt: '2026-11-02T03:00:00Z' }
const printerPage = { schemaVersion: 1, observedAt: '2026-11-02T04:00:00Z', totalCount: 1, activeCount: 1, items: [printerItem], nextAfterPrinterId: null }

const session = {
  serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01',
  deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb',
}
const line = { sellableItemId: 'ITEM-01', quantity: 1, fulfillmentType: 'DineIn' as const, comment: null, options: [] }
const json = (body: unknown, status = 200) => new Response(JSON.stringify(body), { status, headers: { 'Content-Type': 'application/json' } })

test('printer inventory uses authenticated Branch GET, encodes cursors and projects configuration-only public fields', async () => {
  let calls = 0
  const client = createOrderingClient(session, { fetch: async (url, init) => {
    calls++; assert.equal(url, `${session.serviceOrigin}/api/v1/ordering/printer-inventory?afterPrinterId=PRINTER%2F%2B00`)
    assert.equal(init.method, 'GET'); assert.equal(init.body, undefined); assert.equal(init.redirect, 'error'); assert.equal(init.cache, 'no-store'); assert.equal(init.credentials, 'omit')
    const headers = new Headers(init.headers)
    assert.equal(headers.get('Authorization'), `Device ${session.deviceCredential}`); assert.equal(headers.get('X-POS-Employee-Session'), session.employeeSessionToken)
    return json({ ...printerPage, items: [{ ...printerItem, printerId: 'PRINTER/+01', drawerPulseCommandHex: 'PRIVATE', statusCommandProfile: 'PRIVATE', deviceStatus: 'Online' }] })
  } })
  const result = await client.printerInventory('PRINTER/+00')
  assert.deepEqual(result, { kind: 'ok', value: { ...printerPage, items: [{ ...printerItem, printerId: 'PRINTER/+01' }] } })
  for (const cursor of ['', ' PRINTER', 'bad\nID', 'x'.repeat(129)]) await assert.rejects(client.printerInventory(cursor))
  assert.equal(calls, 1)
})

test('printer inventory validates counts, endpoints, capabilities and SQLite UTF-8 key ordering', () => {
  for (const body of [null, [], { ...printerPage, schemaVersion: 2 }, { ...printerPage, activeCount: 2 }, { ...printerPage, activeCount: -1 },
    { ...printerPage, totalCount: 0 }, { ...printerPage, nextAfterPrinterId: 'WRONG' }, { ...printerPage, items: Array(51).fill(printerItem) },
    { ...printerPage, items: [printerItem, printerItem] }]) assert.throws(() => parsePrinterInventoryPage(body))
  for (const patch of [{ port: 0 }, { port: 65536 }, { host: 'tcp://user:PRIVATE@printer.branch.test' }, { host: 'user:PRIVATE' },
    { protocol: 'Unknown' }, { statusMode: 'Online' }, { isActive: 1 }, { supportsCashDrawer: 'true' }, { paperWidthMillimeters: 0 },
    { updatedAt: '2026-02-30T04:00:00Z' }, { printerId: 'bad\nID' }, { displayName: '' }]) assert.throws(() => parsePrinterInventoryPage({ ...printerPage, items: [{ ...printerItem, ...patch }] }))
  for (const host of [null, '192.0.2.10', '2001:db8::10', '[2001:db8::10]', 'fe80::1%3', 'printer.branch.test']) {
    assert.equal(parsePrinterInventoryPage({ ...printerPage, items: [{ ...printerItem, host }] }).items[0].host, host)
  }
  assert.throws(() => parsePrinterInventoryPage(printerPage, printerItem.printerId))
  const bmp = { ...printerItem, printerId: 'PRINTER-\uE000' }, supplementary = { ...printerItem, printerId: 'PRINTER-\u{10000}' }
  const unicode = { ...printerPage, totalCount: 2, activeCount: 2, items: [bmp, supplementary] }
  assert.equal(parsePrinterInventoryPage(unicode).items.length, 2)
  assert.throws(() => parsePrinterInventoryPage({ ...unicode, items: [supplementary, bmp] }))
})

test('printer inventory failures or cancellation never imply no configured printers', async () => {
  for (const status of [401, 403, 404, 503]) {
    const client = createOrderingClient(session, { fetch: async () => json({ error: 'PRIVATE' }, status) })
    assert.deepEqual(await client.printerInventory(), { kind: 'unavailable', reason: 'http', httpStatus: status })
  }
  const malformed = createOrderingClient(session, { fetch: async () => json({ schemaVersion: 1 }) })
  assert.deepEqual(await malformed.printerInventory(), { kind: 'unavailable', reason: 'protocol' })
  const oversize = createOrderingClient(session, { maxResponseBytes: 8, fetch: async () => json(printerPage) })
  assert.deepEqual(await oversize.printerInventory(), { kind: 'unavailable', reason: 'protocol' })
  const abort = new AbortController(); abort.abort()
  assert.deepEqual(await malformed.printerInventory(undefined, abort.signal), { kind: 'unavailable', reason: 'aborted' })
})

test('replay status keeps exact sequences, authenticated read-only routes and public-field projection', async () => {
  let calls = 0
  const client = createOrderingClient(session, { fetch: async (url, init) => {
    calls++
    assert.equal(url, `${session.serviceOrigin}/api/v1/ordering/cloud-replay-status?afterSequence=9007199254740992`)
    assert.equal(init.method, 'GET'); assert.equal(init.body, undefined); assert.equal(init.redirect, 'error'); assert.equal(init.cache, 'no-store'); assert.equal(init.credentials, 'omit')
    const headers = new Headers(init.headers)
    assert.equal(headers.get('Authorization'), `Device ${session.deviceCredential}`); assert.equal(headers.get('X-POS-Employee-Session'), session.employeeSessionToken)
    return json({ ...replayPage, rawError: 'PRIVATE', summary: { ...replayPage.summary, secret: 'PRIVATE' }, items: [{ ...replayItem, leaseOwner: 'PRIVATE', errorCode: 'PRIVATE' }] })
  } })
  assert.deepEqual(await client.cloudReplayStatus('9007199254740992'), { kind: 'ok', value: replayPage })
  for (const invalid of ['', '0', '01', '-1', '1.0', '١', '9223372036854775808']) await assert.rejects(client.cloudReplayStatus(invalid))
  assert.equal(calls, 1)
})

test('replay status validates overlapping counts, sequence ranges, lease fields and keyset pages', () => {
  for (const body of [null, [], { ...replayPage, schemaVersion: 2 }, { ...replayPage, summary: null },
    { ...replayPage, summary: { ...replayPage.summary, pendingCount: 2 } }, { ...replayPage, summary: { ...replayPage.summary, withRetriesCount: 2 } },
    { ...replayPage, nextAfterSequence: '1' }, { ...replayPage, items: Array(51).fill(replayItem) },
    { ...replayPage, items: [replayItem, replayItem] }, { ...replayPage, observedAt: '2026-02-30T04:00:00Z' }]) assert.throws(() => parseCloudReplayPage(body))
  for (const patch of [{ expectedBranchSequence: Number('9007199254740993') }, { receivedBranchSequence: replayItem.expectedBranchSequence },
    { receivedBranchSequence: '9223372036854775808' }, { retryCount: -1 }, { hasLease: true }, { hasLease: 'false' },
    { lastExpectedBranchSequence: replayItem.expectedBranchSequence }, { lastFailedAt: 'PRIVATE' }, { action: '' }, { recoveryId: 'bad\nID' }]) {
    assert.throws(() => parseCloudReplayPage({ ...replayPage, items: [{ ...replayItem, ...patch }] }))
  }
  assert.throws(() => parseCloudReplayPage(replayPage, replayItem.expectedBranchSequence))
  assert.throws(() => parseCloudReplayPage({ ...replayPage, items: [{ ...replayItem, hasLease: true, leaseExpiresAt: '2026-11-02T01:00:00Z' }] }))
  assert.throws(() => parseCloudReplayPage({ ...replayPage, summary: { ...replayPage.summary, withRetriesCount: 0 } }))
  assert.equal(parseCloudReplayPage({ ...replayPage, summary: { ...replayPage.summary, leasedCount: 1, unleasedCount: 0 }, items: [{ ...replayItem, hasLease: true, leaseExpiresAt: '2026-11-02T01:00:00Z' }] }).items[0].hasLease, true)
  const next = { ...replayItem, recoveryId: 'RECOVERY-2', expectedBranchSequence: '9007199254740994' }
  assert.throws(() => parseCloudReplayPage({ ...replayPage, summary: { pendingCount: 2, leasedCount: 0, unleasedCount: 2, withRetriesCount: 2 }, items: [next, replayItem] }))
})

test('replay read failures and cancellation never become an empty successful queue', async () => {
  for (const status of [401, 403, 404, 503]) {
    const client = createOrderingClient(session, { fetch: async () => json({ error: 'PRIVATE' }, status) })
    assert.deepEqual(await client.cloudReplayStatus(), { kind: 'unavailable', reason: 'http', httpStatus: status })
  }
  const malformed = createOrderingClient(session, { fetch: async () => json({ schemaVersion: 1 }) })
  assert.deepEqual(await malformed.cloudReplayStatus(), { kind: 'unavailable', reason: 'protocol' })
  const oversize = createOrderingClient(session, { maxResponseBytes: 8, fetch: async () => json(replayPage) })
  assert.deepEqual(await oversize.cloudReplayStatus(), { kind: 'unavailable', reason: 'protocol' })
  const abort = new AbortController(); abort.abort()
  assert.deepEqual(await malformed.cloudReplayStatus(undefined, abort.signal), { kind: 'unavailable', reason: 'aborted' })
})

test('cloud outbox preserves exact int64 strings and uses authenticated same-origin read-only GET', async () => {
  let calls = 0
  const client = createOrderingClient(session, { fetch: async (url, init) => {
    calls++
    assert.equal(url, `${session.serviceOrigin}/api/v1/ordering/cloud-outbox-status?afterSequence=9007199254740992`)
    assert.equal(init.method, 'GET'); assert.equal(init.body, undefined); assert.equal(init.redirect, 'error'); assert.equal(init.cache, 'no-store'); assert.equal(init.credentials, 'omit')
    const headers = new Headers(init.headers)
    assert.equal(headers.get('Authorization'), `Device ${session.deviceCredential}`); assert.equal(headers.get('X-POS-Employee-Session'), session.employeeSessionToken)
    return json({ ...cloudPage, rawError: 'PRIVATE', summary: { ...cloudPage.summary, secret: 'PRIVATE' }, items: [{ ...cloudItem, lastError: 'PRIVATE', leaseOwner: 'PRIVATE' }] })
  } })
  assert.deepEqual(await client.cloudOutboxStatus('9007199254740992'), { kind: 'ok', value: cloudPage })
  for (const invalid of ['', '0', '01', '-1', '1.0', '١', '9223372036854775808', '../other']) await assert.rejects(client.cloudOutboxStatus(invalid))
  assert.equal(calls, 1)
  assert.equal(parseCloudOutboxPage({ ...cloudPage, items: [{ ...cloudItem, branchSequence: '9223372036854775807' }] }).items[0].branchSequence, '9223372036854775807')
})

test('cloud outbox validates counts, public states, keyset order and continuation boundaries', () => {
  for (const body of [null, [], { ...cloudPage, schemaVersion: 2 }, { ...cloudPage, summary: null },
    { ...cloudPage, summary: { ...cloudPage.summary, unacknowledgedCount: 2 } }, { ...cloudPage, summary: { ...cloudPage.summary, failedCount: -1 } },
    { ...cloudPage, nextAfterSequence: '1' }, { ...cloudPage, items: Array(51).fill(cloudItem) },
    { ...cloudPage, items: [cloudItem, cloudItem] }, { ...cloudPage, observedAt: '2026-02-30T04:00:00Z' }]) assert.throws(() => parseCloudOutboxPage(body))
  for (const patch of [{ branchSequence: Number('9007199254740993') }, { branchSequence: '9223372036854775808' }, { processingStatus: 'Completed' }, { retryCount: -1 },
    { expectedBranchSequence: cloudItem.branchSequence }, { expectedBranchSequence: '0' }, { lastFailedAt: 'PRIVATE' }, { action: '' }, { outboxId: 'bad\nID' }]) {
    assert.throws(() => parseCloudOutboxPage({ ...cloudPage, items: [{ ...cloudItem, ...patch }] }))
  }
  assert.throws(() => parseCloudOutboxPage(cloudPage, cloudItem.branchSequence))
  const second = { ...cloudItem, outboxId: 'EVENT-2', branchSequence: '9007199254740994' }
  assert.throws(() => parseCloudOutboxPage({ ...cloudPage, summary: { ...cloudPage.summary, unacknowledgedCount: 2, failedCount: 2 }, items: [second, cloudItem] }))
  assert.equal(parseCloudOutboxPage({ ...cloudPage, items: [], nextAfterSequence: null }).items.length, 0)
})

test('cloud outbox failures, cancellation and oversized data never become zero-pending success', async () => {
  for (const status of [401, 403, 404, 503]) {
    const client = createOrderingClient(session, { fetch: async () => json({ error: 'PRIVATE' }, status) })
    assert.deepEqual(await client.cloudOutboxStatus(), { kind: 'unavailable', reason: 'http', httpStatus: status })
  }
  const malformed = createOrderingClient(session, { fetch: async () => json({ schemaVersion: 1 }) })
  assert.deepEqual(await malformed.cloudOutboxStatus(), { kind: 'unavailable', reason: 'protocol' })
  const oversize = createOrderingClient(session, { maxResponseBytes: 8, fetch: async () => json(cloudPage) })
  assert.deepEqual(await oversize.cloudOutboxStatus(), { kind: 'unavailable', reason: 'protocol' })
  const abort = new AbortController(); abort.abort()
  assert.deepEqual(await malformed.cloudOutboxStatus(undefined, abort.signal), { kind: 'unavailable', reason: 'aborted' })
})

test('print attention uses bounded authenticated ordering GET routes, validates cursors before I/O and projects public fields', async () => {
  const urls: string[] = []
  const client = createOrderingClient(session, { fetch: async (url, init) => {
    urls.push(url)
    assert.equal(init.method, 'GET'); assert.equal(init.body, undefined)
    assert.equal(init.redirect, 'error'); assert.equal(init.cache, 'no-store'); assert.equal(init.credentials, 'omit')
    const headers = new Headers(init.headers)
    assert.equal(headers.get('Authorization'), `Device ${session.deviceCredential}`)
    assert.equal(headers.get('X-POS-Employee-Session'), session.employeeSessionToken)
    return json({ ...attentionPage, rawError: 'PRIVATE', items: [{ ...attentionItem, lastError: 'PRIVATE', latestErrorCode: 'PRIVATE' }] })
  } })
  assert.deepEqual(await client.printAttention(), { kind: 'ok', value: attentionPage })
  await client.printAttention('NEXT-CURSOR')
  assert.deepEqual(urls, [`${session.serviceOrigin}/api/v1/ordering/print-attention`, `${session.serviceOrigin}/api/v1/ordering/print-attention?cursor=NEXT-CURSOR`])
  for (const cursor of ['', 'a+b', '../escape', 'x'.repeat(1025)]) await assert.rejects(client.printAttention(cursor))
  assert.equal(urls.length, 2)
})

test('print attention rejects malformed, overlarge, duplicate or inconsistent public pages', () => {
  for (const body of [null, [], { ...attentionPage, schemaVersion: 2 }, { ...attentionPage, totalCount: -1 },
    { ...attentionPage, totalCount: Number.MAX_SAFE_INTEGER + 1 }, { ...attentionPage, observedAt: '2026-02-30T04:00:00Z' },
    { ...attentionPage, nextCursor: '' }, { ...attentionPage, items: [] }, { ...attentionPage, items: Array(51).fill(attentionItem) },
    { ...attentionPage, items: [attentionItem, attentionItem] }, { ...attentionPage, totalCount: 0 }]) assert.throws(() => parsePrintAttentionPage(body))
  for (const patch of [{ status: 'Completed' }, { status: 'constructor' }, { attentionReason: 'RawPrinterError' }, { targetStation: '' },
    { assignedPrinterId: ' ' }, { latestAttemptStatus: 'Completed' }, { createdAt: '2026-11-02T03:00:00+07:00' },
    { latestAttemptFinishedAt: 'private' }, { nextRetryAt: 0 }, { purpose: '1' }, { printJobId: 'bad\nID' }]) {
    assert.throws(() => parsePrintAttentionPage({ ...attentionPage, items: [{ ...attentionItem, ...patch }] }))
  }
  assert.deepEqual(parsePrintAttentionPage({ ...attentionPage, totalCount: 0, items: [], nextCursor: null }).items, [])
})

test('print attention maps disabled, unauthorized, failing and malformed responses to unavailable', async () => {
  for (const status of [401, 403, 404, 503]) {
    const client = createOrderingClient(session, { fetch: async () => json({ error: 'PRIVATE' }, status) })
    assert.deepEqual(await client.printAttention(), { kind: 'unavailable', reason: 'http', httpStatus: status })
  }
  const invalid = createOrderingClient(session, { fetch: async () => json({ schemaVersion: 1 }) })
  assert.deepEqual(await invalid.printAttention(), { kind: 'unavailable', reason: 'protocol' })
  const oversize = createOrderingClient(session, { maxResponseBytes: 16, fetch: async () => json(attentionPage) })
  assert.deepEqual(await oversize.printAttention(), { kind: 'unavailable', reason: 'protocol' })
  const canceled = new AbortController(); canceled.abort()
  assert.deepEqual(await invalid.printAttention(undefined, canceled.signal), { kind: 'unavailable', reason: 'aborted' })
})

for (const [method, path, field] of [
  ['dataSyncStatus', 'employee-data-sync-status', 'lastSynchronizedAt'],
  ['dataSentStatus', 'cloud-data-sent-status', 'lastSentAt'],
] as const) {
  test(`${method} uses the authenticated same-origin Branch GET and validates UTC timestamps`, async () => {
    let payload: unknown = { schemaVersion: 1, [field]: '2026-11-02T03:00:00.1234567+00:00' }
    const client = createOrderingClient(session, { fetch: async (url, init) => {
      assert.equal(url, `${session.serviceOrigin}/api/v1/${path}`)
      assert.equal(init.method, 'GET'); assert.equal(init.body, undefined)
      assert.equal(init.credentials, 'omit'); assert.equal(init.cache, 'no-store'); assert.equal(init.redirect, 'error')
      const headers = new Headers(init.headers)
      assert.equal(headers.get('Authorization'), `Device ${session.deviceCredential}`)
      assert.equal(headers.get('X-POS-Employee-Session'), session.employeeSessionToken)
      return json(payload)
    } })
    assert.deepEqual(await client[method](), { kind: 'ok', value: '2026-11-02T03:00:00.1234567+00:00' })
    payload = { schemaVersion: 1, [field]: null }
    assert.deepEqual(await client[method](), { kind: 'ok', value: null })
    for (const invalid of [undefined, '', '2026-11-02T03:00:00', '2026-11-02T03:00:00+07:00', '2026-02-30T03:00:00Z', '2026-11-02T24:00:00Z', 42, {}, 'secret stack trace']) {
      payload = { schemaVersion: 1, [field]: invalid }
      assert.deepEqual(await client[method](), { kind: 'unavailable', reason: 'protocol' })
    }
    for (const invalid of [null, [], { schemaVersion: 2, [field]: null }, { schemaVersion: 1 }]) {
      payload = invalid
      assert.deepEqual(await client[method](), { kind: 'unavailable', reason: 'protocol' })
    }
  })

  test(`${method} fails closed for disabled/unauthorized endpoints, oversized responses and cancellation`, async () => {
    for (const status of [401, 403, 404, 503]) {
      const client = createOrderingClient(session, { fetch: async () => json({ error: 'private diagnostic' }, status) })
      assert.deepEqual(await client[method](), { kind: 'unavailable', reason: 'http', httpStatus: status })
    }
    const client = createOrderingClient(session, { maxResponseBytes: 8, fetch: async () => json({ schemaVersion: 1, [field]: null }) })
    assert.deepEqual(await client[method](), { kind: 'unavailable', reason: 'protocol' })
    const abort = new AbortController(); abort.abort()
    assert.deepEqual(await client[method](abort.signal), { kind: 'unavailable', reason: 'aborted' })
  })
}
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
