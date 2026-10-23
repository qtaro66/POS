import type { Draft } from './draft.ts'
import { isOutstanding, type MobileRequest } from './mobileRequest.ts'
import type { OutboxSummary } from './types.ts'

/** Number of locally held requests the Branch Service has not yet accepted or rejected. */
export function countUnsent(outbox: OutboxSummary): number {
  return outbox.draft + outbox.pending + outbox.submitted + outbox.unknown
}

/**
 * Counts what this device still holds for the Branch Service (D10.5, D20.1):
 * each non-empty Draft (basket) once, and each outstanding request by state.
 * Accepted and rejected requests are not counted.
 */
export function summarizeOutbox(
  drafts: readonly Draft[],
  requests: readonly MobileRequest[],
): OutboxSummary {
  const outstanding = requests.filter(isOutstanding)
  return {
    draft: drafts.filter((draft) => draft.lines.length > 0).length,
    pending: outstanding.filter((request) => request.state === 'Pending').length,
    submitted: outstanding.filter((request) => request.state === 'Submitted').length,
    unknown: outstanding.filter((request) => request.state === 'Unknown').length,
  }
}
