import type { PairedDevice } from '../domain/types.ts'
import type { DeviceStore } from './ports.ts'

const DATABASE_NAME = 'pos-mobile'
const DATABASE_VERSION = 1
const STORE_NAME = 'device'
const RECORD_KEY = 'registration'

interface DeviceRecord {
  readonly device: PairedDevice
  readonly credential: string
}

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

/**
 * Keeps the paired device and its credential in IndexedDB on this device.
 * One record only: pairing again replaces it (requirement 3.3 / D11).
 */
export class IndexedDbDeviceStore implements DeviceStore {
  private readonly factory: IDBFactory
  private database: Promise<IDBDatabase> | null = null

  constructor(factory: IDBFactory = indexedDB) {
    this.factory = factory
  }

  private open(): Promise<IDBDatabase> {
    this.database ??= new Promise((resolve, reject) => {
      const request = this.factory.open(DATABASE_NAME, DATABASE_VERSION)
      request.onupgradeneeded = () => {
        if (!request.result.objectStoreNames.contains(STORE_NAME)) {
          request.result.createObjectStore(STORE_NAME)
        }
      }
      request.onsuccess = () => resolve(request.result)
      request.onerror = () => reject(request.error ?? new Error('IndexedDB could not be opened.'))
    })
    return this.database
  }

  private async read(): Promise<DeviceRecord | null> {
    const database = await this.open()
    const store = database.transaction(STORE_NAME, 'readonly').objectStore(STORE_NAME)
    const value: unknown = await requestResult(store.get(RECORD_KEY))
    if (typeof value !== 'object' || value === null) return null
    const record = value as Partial<DeviceRecord>
    return record.device && typeof record.credential === 'string'
      ? { device: record.device, credential: record.credential }
      : null
  }

  async load(): Promise<PairedDevice | null> {
    return (await this.read())?.device ?? null
  }

  async loadCredential(deviceId: string): Promise<string | null> {
    const record = await this.read()
    return record?.device.deviceId === deviceId ? record.credential : null
  }

  async save(device: PairedDevice, credential: string): Promise<void> {
    const database = await this.open()
    const transaction = database.transaction(STORE_NAME, 'readwrite')
    const done = transactionDone(transaction)
    const record: DeviceRecord = { device, credential }
    transaction.objectStore(STORE_NAME).put(record, RECORD_KEY)
    await done
  }
}
