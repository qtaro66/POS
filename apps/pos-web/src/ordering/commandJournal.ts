import { validateCommandSnapshot, type CommandScope, type CommandSnapshot } from '../api/orderingClient.ts'

export interface JournalEntry { readonly command: CommandSnapshot; readonly state: 'pending' | 'resolved' }
export interface CommandJournal {
  add(command: CommandSnapshot): Promise<boolean>
  load(scope: CommandScope, requestId: string): Promise<JournalEntry | null>
  list(scope: CommandScope): Promise<readonly JournalEntry[]>
  resolve(command: CommandSnapshot): Promise<void>
  finalize(command: CommandSnapshot): Promise<void>
}

const databaseName = 'pos-pc-ordering-journal'
const storeName = 'commands'
const scopeKey = (scope: CommandScope) => [scope.serviceOrigin, scope.branchId, scope.deviceId]
const key = (command: CommandScope & { requestId: string }) => [...scopeKey(command), command.requestId]
const same = (left: CommandSnapshot, right: CommandSnapshot) => JSON.stringify(left) === JSON.stringify(right)

function entry(value: unknown): JournalEntry {
  if (!value || typeof value !== 'object' || !('command' in value) || !('state' in value) || (value.state !== 'pending' && value.state !== 'resolved')) throw new Error('Invalid command journal entry.')
  return { command: validateCommandSnapshot(value.command), state: value.state }
}

/** A separate browser database. No credentials, receipts or financial authority are stored. */
export function createCommandJournal(factory: IDBFactory | undefined = globalThis.indexedDB): CommandJournal {
  let database: Promise<IDBDatabase> | undefined
  function open(): Promise<IDBDatabase> {
    if (!factory) return Promise.reject(new Error('Command recovery storage is unavailable.'))
    if (!database) {
      database = new Promise((resolve, reject) => {
        const request = factory.open(databaseName, 1)
        let settled = false
        request.onupgradeneeded = () => {
          const store = request.result.createObjectStore(storeName, { keyPath: ['command.serviceOrigin', 'command.branchId', 'command.deviceId', 'command.requestId'] })
          store.createIndex('scope', ['command.serviceOrigin', 'command.branchId', 'command.deviceId'])
        }
        request.onerror = request.onblocked = () => { settled = true; reject(new Error('Command recovery storage could not be opened.')) }
        request.onsuccess = () => {
          if (settled) { request.result.close(); return }
          request.result.onversionchange = () => { request.result.close(); database = undefined }
          resolve(request.result)
        }
      })
      database.catch(() => { database = undefined })
    }
    return database
  }

  async function transaction<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore, result: (value: T) => void) => void): Promise<T> {
    const db = await open()
    return new Promise<T>((resolve, reject) => {
      const tx = db.transaction(storeName, mode)
      let value: T
      tx.oncomplete = () => resolve(value)
      tx.onerror = tx.onabort = () => reject(new Error('Command recovery storage transaction failed.'))
      try { work(tx.objectStore(storeName), result => { value = result }) }
      catch { tx.abort() }
    })
  }

  async function change(command: CommandSnapshot, finalize: boolean): Promise<void> {
    const snapshot = validateCommandSnapshot(command)
    await transaction<void>('readwrite', store => {
      const request = store.get(key(snapshot))
      request.onsuccess = () => {
        try {
          const saved = entry(request.result)
          if (!same(saved.command, snapshot) || finalize && saved.state !== 'resolved') throw new Error('Unresolved or changed command.')
          if (finalize) store.delete(key(snapshot))
          else store.put({ command: snapshot, state: 'resolved' })
        } catch { store.transaction.abort() }
      }
    })
  }

  return {
    async add(command) {
      const snapshot = validateCommandSnapshot(command)
      return transaction<boolean>('readwrite', (store, result) => {
        const request = store.get(key(snapshot))
        request.onsuccess = () => {
          try {
            if (request.result !== undefined) {
              if (!same(entry(request.result).command, snapshot)) throw new Error('Request identifier collision.')
              result(false)
            } else { store.add({ command: snapshot, state: 'pending' }); result(true) }
          } catch { store.transaction.abort() }
        }
      })
    },
    async load(scope, requestId) {
      return transaction<JournalEntry | null>('readonly', (store, result) => {
        const request = store.get(key({ ...scope, requestId }))
        request.onsuccess = () => {
          try { result(request.result === undefined ? null : entry(request.result)) }
          catch { store.transaction.abort() }
        }
      })
    },
    async list(scope) {
      return transaction<JournalEntry[]>('readonly', (store, result) => {
        const request = store.index('scope').getAll(scopeKey(scope))
        request.onsuccess = () => {
          try { result(request.result.map(entry)) }
          catch { store.transaction.abort() }
        }
      })
    },
    resolve: command => change(command, false),
    finalize: command => change(command, true),
  }
}
