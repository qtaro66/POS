import { summarizeOutbox } from '../domain/outbox.ts'
import type { OutboxSummary, SyncStatus } from '../domain/types.ts'
import type { OrderOutbox, SyncStatusSource } from './ports.ts'

const EMPTY: OutboxSummary = { draft: 0, pending: 0, submitted: 0, unknown: 0 }

/**
 * Sync status whose outbox counts come from the device's persistent outbox,
 * while connection and employee-data sync still come from `base` (fixtures
 * until CAP-03). Counts are recomputed after every stored change.
 */
export class OutboxSyncStatus implements SyncStatusSource {
  private readonly base: SyncStatusSource
  private readonly outbox: OrderOutbox
  private counts: OutboxSummary = EMPTY
  private status: SyncStatus
  private readonly listeners = new Set<() => void>()
  /** Increases with every refresh so a slow, older read never overwrites a newer one. */
  private generation = 0

  constructor(base: SyncStatusSource, outbox: OrderOutbox) {
    this.base = base
    this.outbox = outbox
    this.status = { ...base.getStatus(), outbox: EMPTY }
    base.subscribe(() => this.publish())
    outbox.subscribe(() => void this.refresh())
  }

  getStatus(): SyncStatus {
    return this.status
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  /** Reads the stored Drafts and requests again; a failed read keeps the last counts. */
  async refresh(): Promise<void> {
    const generation = ++this.generation
    try {
      const [drafts, requests] = await Promise.all([this.outbox.listDrafts(), this.outbox.listRequests()])
      if (generation !== this.generation) return
      this.counts = summarizeOutbox(drafts, requests)
      this.publish()
    } catch {
      // The previous counts stay visible; the next stored change triggers another read.
    }
  }

  private publish(): void {
    this.status = { ...this.base.getStatus(), outbox: this.counts }
    for (const listener of this.listeners) listener()
  }
}
