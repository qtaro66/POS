import { isCashHistoryId } from '../api/cashHistoryContracts.ts'
import { isKitchenReprintCommandId, validateKitchenReprintTarget, type KitchenReprintTarget } from '../api/kitchenReprintContracts.ts'

export interface KitchenReprintRecoveryScope { readonly serviceOrigin: string; readonly branchId: string; readonly deviceId: string; readonly employeeId: string }
export interface KitchenReprintRecoveryEntry { readonly formatVersion: 1; readonly scope: KitchenReprintRecoveryScope; readonly commandRequestId: string; readonly target: KitchenReprintTarget }
export interface KitchenReprintJournal {
  load(): Promise<KitchenReprintRecoveryEntry | null>
  add(command: { commandRequestId: string; target: KitchenReprintTarget }): Promise<void>
  clear(commandRequestId: string): Promise<void>
}
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
function invalid(): never { throw new Error('Kitchen reprint recovery unavailable.') }
function readScope(value: unknown): KitchenReprintRecoveryScope {
  if (!object(value) || typeof value.serviceOrigin !== 'string' || ![value.branchId, value.deviceId, value.employeeId].every(isCashHistoryId)) invalid()
  const origin = new URL(value.serviceOrigin)
  if (origin.protocol !== 'https:' || origin.origin !== value.serviceOrigin || origin.username || origin.password) invalid()
  return Object.freeze({ serviceOrigin: value.serviceOrigin, branchId: value.branchId as string, deviceId: value.deviceId as string, employeeId: value.employeeId as string })
}
const sameScope = (a: KitchenReprintRecoveryScope, b: KitchenReprintRecoveryScope) => a.serviceOrigin === b.serviceOrigin && a.branchId === b.branchId && a.deviceId === b.deviceId && a.employeeId === b.employeeId
export function readKitchenReprintRecovery(value: unknown, expectedScope: KitchenReprintRecoveryScope): KitchenReprintRecoveryEntry {
  if (!object(value) || value.formatVersion !== 1 || Object.keys(value).length !== 4 || !isKitchenReprintCommandId(value.commandRequestId) || !object(value.scope) || Object.keys(value.scope).length !== 4) invalid()
  const scope = readScope(value.scope)
  if (!sameScope(scope, readScope(expectedScope))) invalid()
  return Object.freeze({ formatVersion: 1, scope, commandRequestId: value.commandRequestId, target: validateKitchenReprintTarget(value.target) })
}

/** Only a pending identifier/target is stored. A readwrite transaction serializes
 * competing tabs, and completes before the caller may send its one POST. */
export function createKitchenReprintJournal(scope: KitchenReprintRecoveryScope, factory: IDBFactory | undefined = globalThis.indexedDB): KitchenReprintJournal {
  const owner = readScope(scope), key = [owner.serviceOrigin, owner.branchId, owner.deviceId, owner.employeeId]
  async function transact<T>(mode: IDBTransactionMode, work: (store: IDBObjectStore, result: (value: T) => void) => void): Promise<T> {
    if (!factory) invalid()
    const database = await new Promise<IDBDatabase>((resolve, reject) => {
      let request: IDBOpenDBRequest
      let failed = false
      try { request = factory.open('pos-kitchen-reprint-recovery', 1) } catch { reject(new Error('Kitchen reprint recovery unavailable.')); return }
      request.onupgradeneeded = () => { request.result.createObjectStore('pending') }
      request.onsuccess = () => { if (failed) request.result.close(); else resolve(request.result) }
      request.onerror = request.onblocked = () => { failed = true; reject(new Error('Kitchen reprint recovery unavailable.')) }
    })
    try {
      return await new Promise<T>((resolve, reject) => {
        const transaction = database.transaction('pending', mode)
        let value: T
        transaction.oncomplete = () => resolve(value)
        transaction.onabort = transaction.onerror = () => reject(new Error('Kitchen reprint recovery unavailable.'))
        try { work(transaction.objectStore('pending'), result => { value = result }) } catch { transaction.abort() }
      })
    } finally { database.close() }
  }
  return {
    load: () => transact<KitchenReprintRecoveryEntry | null>('readonly', (store, result) => {
      const read = store.get(key)
      read.onsuccess = () => {
        try { result(read.result === undefined ? null : readKitchenReprintRecovery(read.result, owner)) }
        catch { store.transaction.abort() }
      }
    }),
    async add(command) {
      const entry = readKitchenReprintRecovery({ formatVersion: 1, scope: owner, commandRequestId: command.commandRequestId, target: command.target }, owner)
      await transact<void>('readwrite', store => {
        const read = store.get(key)
        read.onsuccess = () => {
          if (read.result !== undefined) { store.transaction.abort(); return }
          store.add(entry, key)
        }
      })
    },
    async clear(commandRequestId) {
      if (!isKitchenReprintCommandId(commandRequestId)) invalid()
      await transact<void>('readwrite', store => {
        const read = store.get(key)
        read.onsuccess = () => {
          try {
            if (read.result === undefined || readKitchenReprintRecovery(read.result, owner).commandRequestId !== commandRequestId) invalid()
            store.delete(key)
          } catch { store.transaction.abort() }
        }
      })
    },
  }
}
