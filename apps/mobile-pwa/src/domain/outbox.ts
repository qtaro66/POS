import type { OutboxSummary } from './types.ts'

/** Number of locally held requests the Branch Service has not yet accepted or rejected. */
export function countUnsent(outbox: OutboxSummary): number {
  return outbox.draft + outbox.pending + outbox.submitted + outbox.unknown
}
