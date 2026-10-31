import test from 'node:test'
import assert from 'node:assert/strict'
import { createOrderingReadController } from '../src/ordering/readController.ts'
import { createPreviewReadClient, previewDetail } from '../src/ordering/preview.ts'
import type { QueryResult } from '../src/api/orderingClient.ts'
import type { SessionDetail } from '../src/api/orderingContracts.ts'

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

test('read controller loads active tables then authoritative details without changing amounts', async () => {
  const controller = createOrderingReadController(createPreviewReadClient())
  await controller.refresh()
  assert.equal(controller.getSnapshot().detailPhase, 'idle')
  await controller.selectSession('S-A04')
  assert.deepEqual(controller.getSnapshot().detail, previewDetail())
  assert.equal(controller.getSnapshot().detail?.billPreviewTotals?.netTotal, 530)
})

test('rapid selection ignores a late old-table response even when transport ignores abort', async () => {
  const old = deferred<QueryResult<SessionDetail>>()
  const preview = createPreviewReadClient()
  const controller = createOrderingReadController({ ...preview, sessionDetail: async id => id === 'S-A04' ? old.promise : { kind: 'ok', value: previewDetail('S-B12', 'B12') } })
  await controller.refresh()
  const first = controller.selectSession('S-A04')
  await controller.selectSession('S-B12')
  old.resolve({ kind: 'ok', value: previewDetail() }); await first
  assert.equal(controller.getSnapshot().detail?.session.tableId, 'B12')
})

test('refresh hides prior financial data before awaiting reads and keeps it hidden on failure', async () => {
  const preview = createPreviewReadClient()
  let fail = false
  const waiting = deferred<QueryResult<SessionDetail>>()
  const controller = createOrderingReadController({ ...preview, sessionDetail: async id => fail ? waiting.promise : preview.sessionDetail(id) })
  await controller.refresh(); await controller.selectSession('S-A04')
  fail = true
  const refreshing = controller.refresh()
  assert.equal(controller.getSnapshot().detail, null)
  waiting.resolve({ kind: 'unavailable', reason: 'network' }); await refreshing
  assert.equal(controller.getSnapshot().detailPhase, 'unavailable')
  assert.equal(controller.getSnapshot().detail, null)
})

test('unchanged tableVersion does not suppress refreshing newly accepted batches', async () => {
  const preview = createPreviewReadClient()
  let calls = 0
  const controller = createOrderingReadController({ ...preview, sessionDetail: async id => { calls++; return preview.sessionDetail(id) } })
  await controller.refresh(); await controller.selectSession('S-A04'); await controller.refresh()
  assert.equal(calls, 2)
})

test('removed table clears selection and cancelled requests cannot restore data', async () => {
  const preview = createPreviewReadClient()
  let empty = false
  const controller = createOrderingReadController({ ...preview, activeTables: async () => empty ? { kind: 'ok', value: { schemaVersion: 1, includesAvailableTables: false, sessions: [] } } : preview.activeTables() })
  await controller.refresh(); await controller.selectSession('S-A04')
  empty = true; await controller.refresh()
  assert.equal(controller.getSnapshot().selectedSessionId, null)
  assert.equal(controller.getSnapshot().detailPhase, 'notFound')
  assert.equal(controller.getSnapshot().detail, null)
})

test('controller cancellation ignores pending results and permits a fresh mount', async () => {
  const waiting = deferred<QueryResult<SessionDetail>>()
  const preview = createPreviewReadClient()
  let wait = true
  const controller = createOrderingReadController({ ...preview, sessionDetail: async id => wait ? waiting.promise : preview.sessionDetail(id) })
  await controller.refresh()
  const pending = controller.selectSession('S-A04'); controller.cancel()
  waiting.resolve({ kind: 'ok', value: previewDetail() }); await pending
  assert.equal(controller.getSnapshot().detail, null)
  wait = false; await controller.refresh()
  assert.equal(controller.getSnapshot().detailPhase, 'ready')
})
