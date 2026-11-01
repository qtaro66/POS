export const orderingDatabaseName = 'pos-pc-ordering-journal'
export const commandsStore = 'commands'
export const basketsStore = 'baskets'

/** Local schema 2 adds baskets without modifying schema-1 command records. */
export function createOrderingDatabase(factory: IDBFactory | undefined = globalThis.indexedDB) {
  let database: Promise<IDBDatabase> | undefined
  function open(): Promise<IDBDatabase> {
    if (!factory) return Promise.reject(new Error('Ordering recovery storage is unavailable.'))
    if (!database) {
      database = new Promise((resolve, reject) => {
        const request = factory.open(orderingDatabaseName, 2)
        let settled = false
        request.onupgradeneeded = () => {
          const db = request.result
          if (!db.objectStoreNames.contains(commandsStore)) {
            const store = db.createObjectStore(commandsStore, { keyPath: ['command.serviceOrigin', 'command.branchId', 'command.deviceId', 'command.requestId'] })
            store.createIndex('scope', ['command.serviceOrigin', 'command.branchId', 'command.deviceId'])
          }
          if (!db.objectStoreNames.contains(basketsStore)) {
            const store = db.createObjectStore(basketsStore, { keyPath: ['serviceOrigin', 'branchId', 'deviceId', 'employeeId', 'tableSessionId'] })
            store.createIndex('owner', ['serviceOrigin', 'branchId', 'deviceId', 'employeeId'])
            store.createIndex('request', ['serviceOrigin', 'branchId', 'deviceId', 'pendingRequestId'], { unique: true })
          }
        }
        request.onerror = request.onblocked = () => { settled = true; reject(new Error('Ordering recovery storage could not be opened.')) }
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
  return async function transaction<T>(names: string[], mode: IDBTransactionMode, work: (tx: IDBTransaction, result: (value: T) => void) => void): Promise<T> {
    const db = await open()
    return new Promise<T>((resolve, reject) => {
      const tx = db.transaction(names, mode)
      let value: T
      tx.oncomplete = () => resolve(value)
      tx.onerror = tx.onabort = () => reject(new Error('Ordering recovery storage transaction failed.'))
      try { work(tx, result => { value = result }) }
      catch { tx.abort() }
    })
  }
}
