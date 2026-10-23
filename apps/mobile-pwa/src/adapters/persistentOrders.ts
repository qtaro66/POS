import { IndexedDbOrderStore } from './indexedDbOrderStore.ts'
import { OutboxSyncStatus } from './outboxSyncStatus.ts'
import type { MobileServices } from './ports.ts'

/**
 * Replaces the Draft repository and the outbox counts of `services` with the
 * persistent IndexedDB store, for both the fixture and the Branch builds.
 * Drafts older than the retention period are marked for review at start-up.
 */
export async function withPersistentOrders(
  services: MobileServices,
  now: () => Date,
  factory: IDBFactory = indexedDB,
): Promise<MobileServices> {
  const store = new IndexedDbOrderStore(factory)
  const syncStatus = new OutboxSyncStatus(services.syncStatus, store)
  try {
    await store.expireDrafts(now())
  } catch {
    // An unreadable store must not stop the app; the basket reports save failures itself.
  }
  await syncStatus.refresh()
  return { ...services, drafts: store, syncStatus }
}
