import { snapshotOrderLines, validateCommandReceipt, validateCommandSnapshot, type CommandScope, type CommandSnapshot } from '../api/orderingClient.ts'
import type { CommandResponse, OrderLine } from '../api/orderingContracts.ts'
import { readJournalEntry } from './commandJournal.ts'
import { basketsStore, commandsStore, createOrderingDatabase } from './orderingDatabase.ts'

export interface BasketOwner extends CommandScope { readonly employeeId: string }
export interface BasketScope extends BasketOwner { readonly tableSessionId: string }
export interface BasketInput extends BasketScope {
  readonly sessionMasterDataVersion: string
  readonly lines: readonly OrderLine[]
}
export interface BasketVersion { readonly basketId: string; readonly revision: number }
export interface BasketRecord extends BasketInput, BasketVersion {
  readonly formatVersion: 1
  readonly pendingRequestId: string | null
}
export type BasketApplication = { readonly kind: 'accepted'; readonly basket: null } | { readonly kind: 'rejected'; readonly basket: BasketRecord }

const validId = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 128 && value === value.trim() && value !== '.' && value !== '..' && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127)
const validRevision = (value: unknown): value is number => Number.isSafeInteger(value) && (value as number) >= 1
function nextRevision(revision: number) { const next = revision + 1; if (!validRevision(next)) throw new Error('Basket revision exhausted.'); return next }
function copyVersion(value: BasketVersion): BasketVersion {
  if (!value || !validId(value.basketId) || !validRevision(value.revision)) throw new Error('Invalid basket version.')
  return { basketId: value.basketId, revision: value.revision }
}
const ownerKey = (scope: BasketOwner) => [scope.serviceOrigin, scope.branchId, scope.deviceId, scope.employeeId]
const basketKey = (scope: BasketScope) => [...ownerKey(scope), scope.tableSessionId]
const commandKey = (command: CommandScope, requestId: string) => [command.serviceOrigin, command.branchId, command.deviceId, requestId]
const equalLines = (left: readonly OrderLine[], right: readonly OrderLine[]) => JSON.stringify(left) === JSON.stringify(right)

function cleanScope(scope: BasketScope): BasketScope {
  if (!scope || ![scope.branchId, scope.deviceId, scope.employeeId, scope.tableSessionId].every(validId) || typeof scope.serviceOrigin !== 'string') throw new Error('Invalid basket scope.')
  const origin = new URL(scope.serviceOrigin)
  if (origin.protocol !== 'https:' || origin.origin !== scope.serviceOrigin) throw new Error('Invalid basket origin.')
  return { serviceOrigin: scope.serviceOrigin, branchId: scope.branchId, deviceId: scope.deviceId, employeeId: scope.employeeId, tableSessionId: scope.tableSessionId }
}
function cleanInput(input: BasketInput): BasketInput {
  const scope = cleanScope(input)
  if (!validId(input.sessionMasterDataVersion) || !Array.isArray(input.lines)) throw new Error('Invalid basket.')
  const lines = input.lines.length === 0 ? [] : snapshotOrderLines(input.lines)
  if (new TextEncoder().encode(JSON.stringify(lines)).byteLength > 64 * 1024) throw new Error('Basket lines exceed 64 KiB.')
  return { ...scope, sessionMasterDataVersion: input.sessionMasterDataVersion, lines }
}
function readBasket(value: unknown): BasketRecord {
  if (!value || typeof value !== 'object') throw new Error('Invalid stored basket.')
  const saved = value as BasketRecord, input = cleanInput(saved)
  if (saved.formatVersion !== 1 || !validId(saved.basketId) || !validRevision(saved.revision) || saved.pendingRequestId !== null && !validId(saved.pendingRequestId) || !equalLines(input.lines, saved.lines)) throw new Error('Invalid stored basket.')
  return { ...input, formatVersion: 1, basketId: saved.basketId, revision: saved.revision, pendingRequestId: saved.pendingRequestId }
}
function sameVersion(saved: BasketRecord | undefined, expected: BasketVersion | null) {
  return expected === null ? saved === undefined : expected && validId(expected.basketId) && validRevision(expected.revision) && saved?.basketId === expected.basketId && saved.revision === expected.revision
}
function requireEditable(saved: BasketRecord | undefined, expected: BasketVersion | null) {
  if (!sameVersion(saved, expected) || saved?.pendingRequestId) throw new Error('Basket changed or is awaiting a command result.')
}
function requireBound(saved: BasketRecord, expected: BasketVersion) {
  if (!sameVersion(saved, expected) || !saved.pendingRequestId) throw new Error('Basket changed or is not bound to a command.')
}
function matches(command: CommandSnapshot, basket: BasketScope): boolean {
  return basketKey(command).every((value, index) => value === basketKey(basket)[index])
}

/** Local draft storage and atomic draft/journal coordination; never contacts Branch. */
export function createBasketStore(factory: IDBFactory | undefined = globalThis.indexedDB) {
  const transact = createOrderingDatabase(factory)
  return {
    async load(scope: BasketScope): Promise<BasketRecord | null> {
      const clean = cleanScope(scope)
      return transact([basketsStore], 'readonly', (tx, result) => {
        const request = tx.objectStore(basketsStore).get(basketKey(clean))
        request.onsuccess = () => { try { result(request.result === undefined ? null : readBasket(request.result)) } catch { tx.abort() } }
      })
    },
    async list(owner: BasketOwner): Promise<readonly BasketRecord[]> {
      const clean = cleanScope({ ...owner, tableSessionId: 'scope-validation' })
      return transact([basketsStore], 'readonly', (tx, result) => {
        const request = tx.objectStore(basketsStore).index('owner').getAll(ownerKey(clean))
        request.onsuccess = () => { try { result(request.result.map(readBasket)) } catch { tx.abort() } }
      })
    },
    async save(input: BasketInput, expected: BasketVersion | null): Promise<BasketRecord> {
      const snapshot = cleanInput(input), version = expected === null ? null : copyVersion(expected)
      return transact([basketsStore], 'readwrite', (tx, result) => {
        const store = tx.objectStore(basketsStore), request = store.get(basketKey(snapshot))
        request.onsuccess = () => {
          try {
            const saved = request.result === undefined ? undefined : readBasket(request.result)
            requireEditable(saved, version)
            if (saved && saved.sessionMasterDataVersion !== snapshot.sessionMasterDataVersion) throw new Error('Session catalog version cannot change.')
            const next: BasketRecord = { ...snapshot, formatVersion: 1, basketId: saved?.basketId ?? crypto.randomUUID(), revision: nextRevision(saved?.revision ?? 0), pendingRequestId: null }
            store.put(next); result(next)
          } catch { tx.abort() }
        }
      })
    },
    async remove(scope: BasketScope, expected: BasketVersion): Promise<void> {
      const clean = cleanScope(scope), version = copyVersion(expected)
      return transact([basketsStore], 'readwrite', (tx, result) => {
        const store = tx.objectStore(basketsStore), request = store.get(basketKey(clean))
        request.onsuccess = () => {
          try { const saved = readBasket(request.result); requireEditable(saved, version); store.delete(basketKey(clean)); result(undefined) }
          catch { tx.abort() }
        }
      })
    },
    async stage(scope: BasketScope, expected: BasketVersion, value: CommandSnapshot): Promise<BasketRecord> {
      const clean = cleanScope(scope), command = validateCommandSnapshot(value), version = copyVersion(expected)
      if (command.action !== 'SubmitOrder' || !matches(command, clean)) throw new Error('Command does not belong to this basket.')
      return transact([basketsStore, commandsStore], 'readwrite', (tx, result) => {
        const store = tx.objectStore(basketsStore), request = store.get(basketKey(clean))
        request.onsuccess = () => {
          try {
            const saved = readBasket(request.result)
            requireEditable(saved, version)
            if (!equalLines(saved.lines, JSON.parse(command.body).lines)) throw new Error('Command does not match the saved basket.')
            const journal = tx.objectStore(commandsStore), existing = journal.get(commandKey(command, command.requestId))
            existing.onsuccess = () => {
              try {
                if (existing.result !== undefined) { tx.abort(); return }
                const next = { ...saved, revision: nextRevision(saved.revision), pendingRequestId: command.requestId }
                // Both writes commit together, or neither becomes visible after reload.
                journal.add({ command, state: 'pending' }); store.put(next); result(next)
              } catch { tx.abort() }
            }
          } catch { tx.abort() }
        }
      })
    },
    async apply(scope: BasketScope, expected: BasketVersion, receipt: CommandResponse): Promise<BasketApplication> {
      const clean = cleanScope(scope), response = structuredClone(receipt), version = copyVersion(expected)
      return transact([basketsStore, commandsStore], 'readwrite', (tx, result) => {
        const store = tx.objectStore(basketsStore), request = store.get(basketKey(clean))
        request.onsuccess = () => {
          try {
            const saved = readBasket(request.result)
            requireBound(saved, version)
            const journal = tx.objectStore(commandsStore), commandRequest = journal.get(commandKey(saved, saved.pendingRequestId!))
            commandRequest.onsuccess = () => {
              try {
                const entry = readJournalEntry(commandRequest.result)
                if (entry.state !== 'resolved' || entry.command.action !== 'SubmitOrder' || !matches(entry.command, saved) || !equalLines(saved.lines, JSON.parse(entry.command.body).lines)) throw new Error('Unresolved or mismatched journal entry.')
                const checked = validateCommandReceipt(response, entry.command)
                if (checked.result.status === 'BranchAccepted') { store.delete(basketKey(saved)); result({ kind: 'accepted', basket: null }) }
                else {
                  const next = { ...saved, revision: nextRevision(saved.revision), pendingRequestId: null }
                  store.put(next); result({ kind: 'rejected', basket: next })
                }
                journal.delete(commandKey(saved, saved.pendingRequestId!))
              } catch { tx.abort() }
            }
          } catch { tx.abort() }
        }
      })
    },
  }
}
