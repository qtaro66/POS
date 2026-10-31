import test from 'node:test'
import assert from 'node:assert/strict'
import { createRecoveryController } from '../src/ordering/recoveryController.ts'
import { createOrderingClient } from '../src/api/orderingClient.ts'
import type { RecoveryReadPort } from '../src/ordering/recoveryController.ts'
import type { JournalEntry } from '../src/ordering/commandJournal.ts'

const client = createOrderingClient({ serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01', deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, { requestId: () => 'REQUEST-01', now: () => new Date('2026-10-31T10:00:00Z') })
const saved: JournalEntry = { command: client.snapshot(client.prepareOpenTable('A04', 'SESSION-01')), state: 'pending' }
const missing = { result: { kind: 'noDurableResult' as const, requestId: 'REQUEST-01' }, recoveryRecorded: true }
function pending<T>() { let resolve!: (value: T) => void; const promise = new Promise<T>(done => { resolve = done }); return { promise, resolve } }

test('refresh lists local entries without a Branch lookup; explicit check retains NoDurableResult', async () => {
  let calls = 0
  const controller = createRecoveryController({ list: async () => [saved], recover: async () => { calls++; return missing } }, () => new Date('2026-10-31T10:01:00Z'))
  await controller.refresh()
  assert.equal(calls, 0)
  assert.equal(controller.getSnapshot().rows[0].phase, 'unchecked')
  await controller.check('REQUEST-01')
  assert.equal(calls, 1)
  assert.equal(controller.getSnapshot().rows[0].outcome?.result.kind, 'noDurableResult')
  assert.equal(controller.getSnapshot().rows[0].checkedAt, '2026-10-31T10:01:00.000Z')
  await controller.check('UNLISTED')
  assert.equal(calls, 1)
})

test('refresh clears stale entries and failure never becomes an empty successful journal', async () => {
  let fail = false
  const controller = createRecoveryController({ list: async () => { if (fail) throw new Error('Private diagnostic'); return [saved] }, recover: async () => missing })
  await controller.refresh(); fail = true
  const refresh = controller.refresh()
  assert.deepEqual(controller.getSnapshot(), { phase: 'loading', rows: [] })
  await refresh
  assert.deepEqual(controller.getSnapshot(), { phase: 'unavailable', rows: [] })
})

test('late lookup cannot restore entries after refresh or cancellation', async () => {
  const reply = pending<Awaited<ReturnType<RecoveryReadPort['recover']>>>()
  let signal: AbortSignal | undefined
  const controller = createRecoveryController({ list: async () => [saved], recover: async (_id, passed) => { signal = passed; return reply.promise } })
  await controller.refresh()
  const check = controller.check('REQUEST-01')
  await controller.refresh()
  assert.equal(signal?.aborted, true)
  reply.resolve(missing); await check
  assert.equal(controller.getSnapshot().rows[0].phase, 'unchecked')
  const waiting = pending<readonly JournalEntry[]>()
  const second = createRecoveryController({ list: () => waiting.promise, recover: async () => missing })
  const refresh = second.refresh(); second.cancel(); waiting.resolve([saved]); await refresh
  assert.equal(second.getSnapshot().rows.length, 0)
})

test('newer lookup supersedes older response and previous outcome is hidden while checking', async () => {
  const old = pending<Awaited<ReturnType<RecoveryReadPort['recover']>>>()
  let calls = 0
  const controller = createRecoveryController({ list: async () => [saved], recover: async () => ++calls === 1 ? old.promise : missing })
  await controller.refresh()
  const previous = controller.check('REQUEST-01')
  await controller.check('REQUEST-01')
  old.resolve({ result: { kind: 'unavailable', reason: 'network' }, recoveryRecorded: true }); await previous
  assert.equal(controller.getSnapshot().rows[0].outcome?.result.kind, 'noDurableResult')
  const check = controller.check('REQUEST-01')
  assert.equal(controller.getSnapshot().rows[0].outcome, undefined)
  await check
})

test('malformed and duplicate persisted snapshots fail closed', async () => {
  for (const entries of [[saved, saved], [{ ...saved, command: { ...saved.command, body: '{broken' } }]]) {
    const controller = createRecoveryController({ list: async () => entries, recover: async () => missing })
    await controller.refresh()
    assert.deepEqual(controller.getSnapshot(), { phase: 'unavailable', rows: [] })
  }
})

test('lookup exceptions yield generic unavailable state without private error details', async () => {
  const controller = createRecoveryController({ list: async () => [saved], recover: async () => { throw new Error('Private diagnostic') } })
  await controller.refresh(); await controller.check('REQUEST-01')
  assert.equal(controller.getSnapshot().rows[0].phase, 'unavailable')
  assert.ok(!JSON.stringify(controller.getSnapshot()).includes('Private diagnostic'))
})
