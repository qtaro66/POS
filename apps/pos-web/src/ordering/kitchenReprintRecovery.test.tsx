import { IDBFactory } from 'fake-indexeddb'
import { expect, it } from 'vitest'
import { createKitchenReprintJournal, readKitchenReprintRecovery } from './kitchenReprintRecovery.ts'

const scope = { serviceOrigin: 'https://branch.example.test', branchId: 'branch', deviceId: 'device', employeeId: 'employee' }
const target = { cashRequestId: 'cash', snapshotId: 'snapshot', orderLineId: 'line', originalPrintJobId: 'original', tableSessionId: 'session', orderBatchId: 'batch', productionStationId: 'station' }
const commandRequestId = '01234567-89ab-cdef-0123-456789abcdef'
const command = () => ({ commandRequestId, target: { ...target } })
const entry = () => ({ formatVersion: 1, scope: { ...scope }, ...command() })

it('commits one PIN-free pending entry and restores it without any mutation transport', async () => {
  const factory = new IDBFactory(), journal = createKitchenReprintJournal({ ...scope, deviceCredential: 'not-stored', employeeSessionToken: 'not-stored' } as typeof scope, factory)
  expect(await journal.load()).toBeNull()
  const pending = command(); const added = journal.add(pending); pending.target.orderLineId = 'changed-after-call'; await added
  expect(await createKitchenReprintJournal(scope, factory).load()).toEqual(entry())
  expect(JSON.stringify(await journal.load())).not.toMatch(/pin|reason|token|credential|not-stored/i)
  await expect(journal.add(command())).rejects.toThrow()
  await journal.clear(commandRequestId); expect(await journal.load()).toBeNull()
})

it('serializes competing tabs and preserves the winning target instead of overwriting it', async () => {
  const factory = new IDBFactory(), a = createKitchenReprintJournal(scope, factory), b = createKitchenReprintJournal(scope, factory)
  const other = { ...command(), commandRequestId: '11111111-1111-1111-1111-111111111111' }
  const results = await Promise.allSettled([a.add(command()), b.add(other)])
  expect(results.filter(result => result.status === 'fulfilled')).toHaveLength(1)
  expect(results.filter(result => result.status === 'rejected')).toHaveLength(1)
  const saved = await a.load(); expect([commandRequestId, other.commandRequestId]).toContain(saved?.commandRequestId)
  await expect(a.clear(saved?.commandRequestId === commandRequestId ? other.commandRequestId : commandRequestId)).rejects.toThrow()
  expect(await b.load()).toEqual(saved)
})

it('isolates origin, Branch, terminal and employee independently', async () => {
  const factory = new IDBFactory(), journal = createKitchenReprintJournal(scope, factory); await journal.add(command())
  for (const other of [{ serviceOrigin: 'https://other.example.test' }, { branchId: 'other' }, { deviceId: 'other' }, { employeeId: 'other' }]) {
    const isolated = createKitchenReprintJournal({ ...scope, ...other }, factory)
    expect(await isolated.load()).toBeNull(); await isolated.add(command()); expect(await journal.load()).toEqual(entry())
  }
})

it('fails closed on unavailable storage and invalid/cross-owner records', async () => {
  const factory = { open: () => { throw new Error('Storage disabled') } } as unknown as IDBFactory
  await expect(createKitchenReprintJournal(scope, factory).add(command())).rejects.toThrow()
  await expect(createKitchenReprintJournal(scope, factory).load()).rejects.toThrow()
  for (const value of [{ ...entry(), pin: 'forbidden' }, { ...entry(), scope: { ...scope, employeeId: 'other' } }, { ...entry(), target: { ...target, reason: 'forbidden' } }, { ...entry(), commandRequestId: 'bad' }]) {
    expect(() => readKitchenReprintRecovery(value, scope)).toThrow()
  }
})

it('does not erase or repair a corrupted pending record', async () => {
  const factory = new IDBFactory(), journal = createKitchenReprintJournal(scope, factory); await journal.add(command())
  const database = await new Promise<IDBDatabase>((resolve, reject) => { const request = factory.open('pos-kitchen-reprint-recovery', 1); request.onsuccess = () => resolve(request.result); request.onerror = () => reject(request.error) })
  await new Promise<void>((resolve, reject) => { const tx = database.transaction('pending', 'readwrite'); tx.objectStore('pending').put({ ...entry(), target: { ...target, orderLineId: '' } }, Object.values(scope)); tx.oncomplete = () => resolve(); tx.onerror = () => reject(tx.error) })
  database.close()
  await expect(journal.load()).rejects.toThrow(); await expect(journal.clear(commandRequestId)).rejects.toThrow(); await expect(journal.add(command())).rejects.toThrow()
})
