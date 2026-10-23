import { applyDraftExpiry, type Draft, type DraftKey } from '../domain/draft.ts'
import type { MobileRequest } from '../domain/mobileRequest.ts'
import type { DraftRepository, OrderOutbox } from './ports.ts'

/*
 * Drafts and outbox requests in their own IndexedDB database, separate from
 * the device registration, so the two schemas can evolve independently.
 * Clearing the browser's site data removes both (D10.5: unsent Drafts are
 * device-only data and may be lost that way).
 */
const DATABASE_NAME = 'pos-mobile-orders'
const DATABASE_VERSION = 1
const DRAFTS = 'drafts'
const REQUESTS = 'requests'

function requestResult<T>(request: IDBRequest<T>): Promise<T> {
  return new Promise((resolve, reject) => {
    request.onsuccess = () => resolve(request.result)
    request.onerror = () => reject(request.error ?? new Error('IndexedDB request failed.'))
  })
}

function transactionDone(transaction: IDBTransaction): Promise<void> {
  return new Promise((resolve, reject) => {
    transaction.oncomplete = () => resolve()
    transaction.onerror = () => reject(transaction.error ?? new Error('IndexedDB write failed.'))
    transaction.onabort = () => reject(transaction.error ?? new Error('IndexedDB write aborted.'))
  })
}

function draftKeyPath(key: DraftKey): [string, string, string] {
  return [key.deviceId, key.employeeId, key.tableSessionId]
}

/**
 * Persistent Draft repository and order outbox (D10.5, D12.2, D20.1). Writes
 * complete before their promise resolves, so a confirmation shown after
 * `save` means the data is on the device.
 */
export class IndexedDbOrderStore implements DraftRepository, OrderOutbox {
  readonly persistence = 'device'
  private readonly factory: IDBFactory
  private database: Promise<IDBDatabase> | null = null
  private readonly listeners = new Set<() => void>()

  constructor(factory: IDBFactory = indexedDB) {
    this.factory = factory
  }

  private open(): Promise<IDBDatabase> {
    this.database ??= new Promise((resolve, reject) => {
      const request = this.factory.open(DATABASE_NAME, DATABASE_VERSION)
      request.onupgradeneeded = () => {
        const database = request.result
        if (!database.objectStoreNames.contains(DRAFTS)) {
          database.createObjectStore(DRAFTS, { keyPath: ['deviceId', 'employeeId', 'tableSessionId'] })
        }
        if (!database.objectStoreNames.contains(REQUESTS)) {
          database.createObjectStore(REQUESTS, { keyPath: 'requestId' })
        }
      }
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error ?? new Error('IndexedDB could not be opened.'))
    })
    return this.database
  }

  private changed(): void {
    for (const listener of this.listeners) listener()
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  async load(key: DraftKey): Promise<Draft | null> {
    const database = await this.open()
    const store = database.transaction(DRAFTS, 'readonly').objectStore(DRAFTS)
    const value = (await requestResult(store.get(draftKeyPath(key)))) as Draft | undefined
    return value ?? null
  }

  async save(draft: Draft): Promise<void> {
    const database = await this.open()
    const transaction = database.transaction(DRAFTS, 'readwrite')
    const done = transactionDone(transaction)
    transaction.objectStore(DRAFTS).put(draft)
    await done
    this.changed()
  }

  async listDrafts(): Promise<readonly Draft[]> {
    const database = await this.open()
    const store = database.transaction(DRAFTS, 'readonly').objectStore(DRAFTS)
    return (await requestResult(store.getAll())) as Draft[]
  }

  async listRequests(): Promise<readonly MobileRequest[]> {
    const database = await this.open()
    const store = database.transaction(REQUESTS, 'readonly').objectStore(REQUESTS)
    return (await requestResult(store.getAll())) as MobileRequest[]
  }

  async getRequest(requestId: string): Promise<MobileRequest | null> {
    const database = await this.open()
    const store = database.transaction(REQUESTS, 'readonly').objectStore(REQUESTS)
    const value = (await requestResult(store.get(requestId))) as MobileRequest | undefined
    return value ?? null
  }

  async submitDraft(key: DraftKey, request: MobileRequest): Promise<void> {
    const database = await this.open()
    // One transaction: the Draft is never lost and the order never appears twice.
    const transaction = database.transaction([DRAFTS, REQUESTS], 'readwrite')
    const done = transactionDone(transaction)
    // `add` fails if the requestId already exists, which aborts the whole transaction.
    transaction.objectStore(REQUESTS).add(request)
    transaction.objectStore(DRAFTS).delete(draftKeyPath(key))
    await done
    this.changed()
  }

  async updateRequest(request: MobileRequest): Promise<void> {
    const database = await this.open()
    const transaction = database.transaction(REQUESTS, 'readwrite')
    const done = transactionDone(transaction)
    const store = transaction.objectStore(REQUESTS)
    const existing = await requestResult(store.getKey(request.requestId))
    if (existing === undefined) {
      transaction.abort()
      await done.catch(() => undefined)
      throw new Error(`No outbox request ${request.requestId} to update.`)
    }
    store.put(request)
    await done
    this.changed()
  }

  async expireDrafts(now: Date): Promise<number> {
    const database = await this.open()
    const transaction = database.transaction(DRAFTS, 'readwrite')
    const done = transactionDone(transaction)
    const store = transaction.objectStore(DRAFTS)
    const drafts = (await requestResult(store.getAll())) as Draft[]
    let expired = 0
    for (const draft of drafts) {
      const next = applyDraftExpiry(draft, now)
      if (next !== draft) {
        store.put(next)
        expired += 1
      }
    }
    await done
    if (expired > 0) this.changed()
    return expired
  }
}
