import { validateCommandSnapshot, type CommandScope, type CommandSnapshot } from '../api/orderingClient.ts'
import { basketsStore, commandsStore, createOrderingDatabase } from './orderingDatabase.ts'

export interface JournalEntry { readonly command: CommandSnapshot; readonly state: 'pending' | 'resolved' }
export interface CommandJournal {
  add(command: CommandSnapshot): Promise<boolean>
  load(scope: CommandScope, requestId: string): Promise<JournalEntry | null>
  list(scope: CommandScope): Promise<readonly JournalEntry[]>
  resolve(command: CommandSnapshot): Promise<void>
  finalize(command: CommandSnapshot): Promise<void>
}

const scopeKey = (scope: CommandScope) => [scope.serviceOrigin, scope.branchId, scope.deviceId]
const key = (command: CommandScope & { requestId: string }) => [...scopeKey(command), command.requestId]
const same = (left: CommandSnapshot, right: CommandSnapshot) => JSON.stringify(left) === JSON.stringify(right)

export function readJournalEntry(value: unknown): JournalEntry {
  if (!value || typeof value !== 'object' || !('command' in value) || !('state' in value) || (value.state !== 'pending' && value.state !== 'resolved')) throw new Error('Invalid command journal entry.')
  return { command: validateCommandSnapshot(value.command), state: value.state }
}

/** A separate browser database. No credentials, receipts or financial authority are stored. */
export function createCommandJournal(factory: IDBFactory | undefined = globalThis.indexedDB): CommandJournal {
  const transact = createOrderingDatabase(factory)
  function transaction<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore, result: (value: T) => void) => void, includeBaskets = false) {
    return transact<T>(includeBaskets ? [commandsStore, basketsStore] : [commandsStore], mode, (tx, result) => work(tx.objectStore(commandsStore), result))
  }

  async function change(command: CommandSnapshot, finalize: boolean): Promise<void> {
    const snapshot = validateCommandSnapshot(command)
    await transaction<void>('readwrite', store => {
      const request = store.get(key(snapshot))
      request.onsuccess = () => {
        try {
          const saved = readJournalEntry(request.result)
          if (!same(saved.command, snapshot) || finalize && saved.state !== 'resolved') throw new Error('Unresolved or changed command.')
          if (finalize) {
            const bound = store.transaction.objectStore(basketsStore).index('request').count(key(snapshot))
            bound.onsuccess = () => { if (bound.result > 0) store.transaction.abort(); else store.delete(key(snapshot)) }
          }
          else store.put({ command: snapshot, state: 'resolved' })
        } catch { store.transaction.abort() }
      }
    }, finalize)
  }

  return {
    async add(command) {
      const snapshot = validateCommandSnapshot(command)
      return transaction<boolean>('readwrite', (store, result) => {
        const request = store.get(key(snapshot))
        request.onsuccess = () => {
          try {
            if (request.result !== undefined) {
              if (!same(readJournalEntry(request.result).command, snapshot)) throw new Error('Request identifier collision.')
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
          try { result(request.result === undefined ? null : readJournalEntry(request.result)) }
          catch { store.transaction.abort() }
        }
      })
    },
    async list(scope) {
      return transaction<JournalEntry[]>('readonly', (store, result) => {
        const request = store.index('scope').getAll(scopeKey(scope))
        request.onsuccess = () => {
          try { result(request.result.map(readJournalEntry)) }
          catch { store.transaction.abort() }
        }
      })
    },
    resolve: command => change(command, false),
    finalize: command => change(command, true),
  }
}
