import { describe, expect, it } from 'vitest'
import { createOrderingClient } from './orderingClient.ts'
import { isKitchenReprintReason, parseKitchenReprintReceipt, parseKitchenReprintStatus, validateKitchenReprintTarget, type KitchenReprintTarget } from './kitchenReprintContracts.ts'

const scope = { serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'device', employeeId: 'employee' }
const session = { ...scope, deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }
const command = '01234567-89ab-cdef-0123-456789abcdef'
const target: KitchenReprintTarget = { cashRequestId: 'cash/+1', snapshotId: 'source/+1', orderLineId: 'line/+1', originalPrintJobId: 'original/+1', tableSessionId: 'session', orderBatchId: 'batch', productionStationId: 'station' }
const receipt = () => ({ schemaVersion: 1, branchId: scope.branchId, requestId: command, cashRequestId: target.cashRequestId,
  tableSessionId: target.tableSessionId, orderBatchId: target.orderBatchId, orderLineId: target.orderLineId, originalSnapshotId: target.snapshotId,
  originalPrintJobId: target.originalPrintJobId, reprintSnapshotId: `branch:${command}:kitchen-reprint-snapshot:1`,
  reprintPrintJobId: `branch:${command}:kitchen-reprint-print:1`, productionStationId: target.productionStationId, queuedAt: '2026-11-28T01:00:00.0000000+00:00' })
const status = () => ({ schemaVersion: 1, branchId: 'branch', asOf: '2026-11-28T01:00:01Z', receipt: receipt(), status: 'Unknown', attemptCount: 1 })
const json = (value: unknown, code = 200) => new Response(JSON.stringify(value), { status: code, headers: { 'Content-Type': 'application/json' } })

describe('kitchen reprint evidence', () => {
  it('binds every frozen target field and projects receipt/status without private extras', () => {
    expect(parseKitchenReprintReceipt({ ...receipt(), pin: 'secret' }, 'branch', command, target)).toEqual(receipt())
    expect(parseKitchenReprintStatus({ ...status(), workerId: 'private' }, 'branch', command, target)).toEqual(status())
    for (const field of ['cashRequestId', 'tableSessionId', 'orderBatchId', 'orderLineId', 'originalSnapshotId', 'originalPrintJobId', 'productionStationId', 'branchId', 'requestId', 'reprintSnapshotId', 'reprintPrintJobId']) {
      expect(() => parseKitchenReprintReceipt({ ...receipt(), [field]: 'foreign' }, 'branch', command, target)).toThrow()
    }
  })
  it.each([{ schemaVersion: 2 }, { queuedAt: '2026-02-30T00:00:00Z' }, { queuedAt: '2026-11-28T01:00:00' }, { queuedAt: '2026-11-28T02:00:00+01:00' }, { originalPrintJobId: 'bad\u0085id' }])('rejects malformed receipt %j', patch => {
    expect(() => parseKitchenReprintReceipt({ ...receipt(), ...patch }, 'branch', command, target)).toThrow()
  })
  it.each([{ status: 'Printed' }, { attemptCount: -1 }, { attemptCount: 1.5 }, { attemptCount: 2147483648 }, { asOf: 'bad' }, { branchId: 'foreign' }])('rejects malformed status %j', patch => {
    expect(() => parseKitchenReprintStatus({ ...status(), ...patch }, 'branch', command, target)).toThrow()
  })
  it('requires one canonical target, command and bounded plain text reason', () => {
    expect(() => validateKitchenReprintTarget({ ...target, pin: 'extra' })).toThrow()
    expect(() => validateKitchenReprintTarget({ ...target, orderLineId: ' '.repeat(2) })).toThrow()
    expect(() => parseKitchenReprintReceipt(receipt(), 'branch', command.toUpperCase(), target)).toThrow()
    const prefix = 'เดิม\r\n\t'
    expect(isKitchenReprintReason(prefix + 'ก'.repeat(4096 - prefix.length))).toBe(true)
    for (const value of ['', ' \t', 'a\u0085b', 'a\u007fb', 'x'.repeat(4097), null]) expect(isKitchenReprintReason(value)).toBe(false)
  })
})

describe('kitchen reprint transport', () => {
  it('sends one explicit authenticated POST, encodes opaque path IDs and exposes a credential-free recovery scope', async () => {
    const calls: { url: string; init: RequestInit }[] = []
    const client = createOrderingClient(session, { fetch: async (url, init) => { calls.push({ url, init }); return json(receipt()) } })
    expect(client.kitchenReprintScope).toEqual(scope)
    expect(await client.kitchenReprint(target, command, 'reason', '123456')).toEqual({ kind: 'queued', receipt: receipt() })
    expect(calls).toHaveLength(1)
    expect(calls[0].url).toBe('https://branch.example.test/api/v1/ordering/transaction-history/cash-sales/cash%2F%2B1/production-orders/source%2F%2B1/lines/line%2F%2B1/reprints')
    expect(calls[0].init).toMatchObject({ method: 'POST', cache: 'no-store', redirect: 'error', credentials: 'omit' })
    expect(JSON.parse(calls[0].init.body as string)).toEqual({ schemaVersion: 1, commandRequestId: command, originalPrintJobId: target.originalPrintJobId, reason: 'reason', pin: '123456' })
    expect(new Headers(calls[0].init.headers).get('X-POS-Employee-Session')).toBe('v1.bbbb')
    expect(new Headers(calls[0].init.headers).get('Authorization')).toBe('Device v1.aaaa')
  })
  it.each([400, 401, 403, 404, 413])('returns a definitive rejection only for %i', async code => {
    let calls = 0
    const client = createOrderingClient(session, { fetch: async () => { calls++; return json({ error: 'Denied' }, code) } })
    expect(await client.kitchenReprint(target, command, 'reason', '123456')).toEqual({ kind: 'rejected', httpStatus: code })
    expect(calls).toBe(1)
  })
  it.each([409, 500, 503])('keeps potentially accepted command %i uncertain and never retries', async code => {
    let calls = 0
    const client = createOrderingClient(session, { fetch: async () => { calls++; return json({}, code) } })
    expect(await client.kitchenReprint(target, command, 'reason', '123456')).toEqual({ kind: 'uncertain', reason: 'http', httpStatus: code })
    expect(calls).toBe(1)
  })
  it('never confirms malformed or foreign success and does not replay a lost response', async () => {
    for (const response of [{ ...receipt(), orderLineId: 'other' }, { ...receipt(), reprintPrintJobId: 'invented' }]) {
      let calls = 0; const client = createOrderingClient(session, { fetch: async () => { calls++; return json(response) } })
      expect(await client.kitchenReprint(target, command, 'reason', '123456')).toEqual({ kind: 'uncertain', reason: 'protocol' }); expect(calls).toBe(1)
    }
    let calls = 0
    const client = createOrderingClient(session, { fetch: async () => { calls++; throw new TypeError('Lost response') } })
    expect(await client.kitchenReprint(target, command, 'reason', '123456')).toEqual({ kind: 'uncertain', reason: 'network' }); expect(calls).toBe(1)
  })
  it('validates locally before sending and cancellation cannot trigger a POST', async () => {
    let calls = 0; const client = createOrderingClient(session, { fetch: async () => { calls++; return json(receipt()) } })
    for (const [id, reason, pin] of [['bad', 'reason', '123456'], [command, '', '123456'], [command, 'reason', ''], [command, 'reason', 'x'.repeat(129)]]) {
      expect(await client.kitchenReprint(target, id, reason, pin)).toEqual({ kind: 'rejected', httpStatus: 400 })
    }
    expect(await client.kitchenReprint(target, command, 'reason', '123456', AbortSignal.abort())).toEqual({ kind: 'uncertain', reason: 'aborted' })
    expect(calls).toBe(0)
  })
  it('recovers with GET only, distinguishes missing from unavailable and validates identity', async () => {
    const calls: RequestInit[] = []
    const client = createOrderingClient(session, { fetch: async (_url, init) => { calls.push(init); return json(status()) } })
    expect(await client.kitchenReprintStatus(command)).toEqual({ kind: 'ok', value: status() })
    expect(calls[0]).toMatchObject({ method: 'GET', body: undefined })
    expect(await createOrderingClient(session, { fetch: async () => json({}, 404) }).kitchenReprintStatus(command)).toEqual({ kind: 'notFound' })
    expect(await createOrderingClient(session, { fetch: async () => json({}, 503) }).kitchenReprintStatus(command)).toEqual({ kind: 'unavailable', reason: 'http', httpStatus: 503 })
    expect(await createOrderingClient(session, { fetch: async () => json({ ...status(), receipt: { ...receipt(), requestId: 'foreign' } }) }).kitchenReprintStatus(command)).toEqual({ kind: 'unavailable', reason: 'protocol' })
    expect(await client.kitchenReprintStatus('bad')).toEqual({ kind: 'unavailable', reason: 'protocol' }); expect(calls).toHaveLength(1)
  })
})
