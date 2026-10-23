// @vitest-environment node
import { IDBFactory } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { addLine, emptyDraft, type Draft, type DraftLine } from '../domain/draft.ts'
import { newSubmitOrderRequest, transitionRequest } from '../domain/mobileRequest.ts'
import { summarizeOutbox } from '../domain/outbox.ts'
import { IndexedDbOrderStore } from './indexedDbOrderStore.ts'

const NOW = new Date('2026-01-15T03:00:00.000Z')
const DAY_MS = 24 * 60 * 60 * 1000
const key = { deviceId: 'device-1', employeeId: 'employee-1', tableSessionId: 'session-1' }
const water: DraftLine = {
  lineId: 'line-1',
  sellableItemId: 'fixture-item-water',
  quantity: 2,
  selections: [],
  comment: '',
}

function draftWithWater(draftKey = key, now = NOW): Draft {
  return addLine(emptyDraft(draftKey, 'md-1', now), water)
}

describe('IndexedDbOrderStore drafts', () => {
  it('reports device persistence', () => {
    expect(new IndexedDbOrderStore(new IDBFactory()).persistence).toBe('device')
  })

  it('keeps a Draft across store instances, as after an app restart (D10.5)', async () => {
    const factory = new IDBFactory()
    await new IndexedDbOrderStore(factory).save(draftWithWater())

    const reopened = new IndexedDbOrderStore(factory)
    expect(await reopened.load(key)).toEqual(draftWithWater())
  })

  it('keeps Drafts apart by device, employee and table session', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    await store.save(draftWithWater())
    expect(await store.load({ ...key, employeeId: 'employee-2' })).toBeNull()
    expect(await store.load({ ...key, tableSessionId: 'session-2' })).toBeNull()
    expect(await store.load({ ...key, deviceId: 'device-2' })).toBeNull()
  })

  it('replaces a Draft when it is saved again', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    await store.save(draftWithWater())
    await store.save({ ...draftWithWater(), lines: [] })
    expect((await store.load(key))?.lines).toEqual([])
    expect(await store.listDrafts()).toHaveLength(1)
  })

  it('marks Drafts older than 30 days for review without deleting them (D19.4)', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    const old = draftWithWater(key, new Date(NOW.getTime() - 30 * DAY_MS))
    const recent = draftWithWater({ ...key, tableSessionId: 'session-2' }, new Date(NOW.getTime() - 29 * DAY_MS))
    await store.save(old)
    await store.save(recent)

    expect(await store.expireDrafts(NOW)).toBe(1)
    expect((await store.load(key))?.reviewState).toBe('ExpiredNeedsReview')
    expect((await store.load(key))?.lines).toEqual([water])
    expect((await store.load(recent))?.reviewState).toBe('Active')
    expect(await store.expireDrafts(NOW)).toBe(0)
  })
})

describe('IndexedDbOrderStore outbox', () => {
  const request = newSubmitOrderRequest('request-1', draftWithWater(), NOW)

  it('turns a Draft into a Pending request in one step, keeping its lines', async () => {
    const factory = new IDBFactory()
    const store = new IndexedDbOrderStore(factory)
    await store.save(draftWithWater())
    await store.submitDraft(key, request)

    const reopened = new IndexedDbOrderStore(factory)
    expect(await reopened.load(key)).toBeNull()
    const stored = await reopened.getRequest('request-1')
    expect(stored?.state).toBe('Pending')
    expect(stored?.lines).toEqual([water])
    expect(stored?.employeeId).toBe('employee-1')
  })

  it('never stores two requests with the same requestId and keeps the Draft when that fails', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    await store.save(draftWithWater())
    await store.submitDraft(key, request)
    const second = { ...key, tableSessionId: 'session-2' }
    await store.save(draftWithWater(second))

    await expect(store.submitDraft(second, { ...request, tableSessionId: 'session-2' })).rejects.toBeTruthy()
    expect(await store.load(second)).not.toBeNull()
    expect(await store.listRequests()).toHaveLength(1)
  })

  it('stores state changes of a request and refuses an unknown requestId', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    await store.submitDraft(key, request)
    const submitted = transitionRequest(request, { type: 'sendStarted' }, NOW)
    await store.updateRequest(submitted)
    expect((await store.getRequest('request-1'))?.state).toBe('Submitted')

    await expect(store.updateRequest({ ...submitted, requestId: 'missing' })).rejects.toThrow(
      /No outbox request missing/,
    )
    expect(await store.listRequests()).toHaveLength(1)
  })

  it('feeds the outbox counts and notifies listeners after each change', async () => {
    const store = new IndexedDbOrderStore(new IDBFactory())
    let notifications = 0
    const unsubscribe = store.subscribe(() => {
      notifications += 1
    })
    await store.save(draftWithWater({ ...key, tableSessionId: 'session-2' }))
    await store.submitDraft(key, request)
    await store.updateRequest(transitionRequest(request, { type: 'sendStarted' }, NOW))

    expect(summarizeOutbox(await store.listDrafts(), await store.listRequests())).toEqual({
      draft: 1,
      pending: 0,
      submitted: 1,
      unknown: 0,
    })
    expect(notifications).toBe(3)
    unsubscribe()
    await store.save(draftWithWater())
    expect(notifications).toBe(3)
  })
})
