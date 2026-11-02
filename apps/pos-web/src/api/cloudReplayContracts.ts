import { isUtcTimestamp } from './printAttentionContracts.ts'
import { isOutboxSequence } from './cloudOutboxContracts.ts'

export interface CloudReplayItem {
  readonly recoveryId: string; readonly outboxId: string; readonly expectedBranchSequence: string; readonly receivedBranchSequence: string
  readonly requestId: string; readonly tableSessionId: string; readonly action: string; readonly requestedAt: string
  readonly hasLease: boolean; readonly retryCount: number; readonly nextRetryAt: string | null; readonly leaseExpiresAt: string | null
  readonly lastFailedAt: string | null; readonly lastExpectedBranchSequence: string | null
}
export interface CloudReplayPage {
  readonly schemaVersion: 1; readonly observedAt: string
  readonly summary: { readonly pendingCount: number; readonly leasedCount: number; readonly unleasedCount: number; readonly withRetriesCount: number }
  readonly items: readonly CloudReplayItem[]; readonly nextAfterSequence: string | null
}
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }
function id(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && value.length <= 128 && value === value.trim() && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) }
function counts(value: unknown): boolean { return record(value) && ['pendingCount', 'leasedCount', 'unleasedCount', 'withRetriesCount'].every(key => Number.isSafeInteger(value[key]) && (value[key] as number) >= 0) }
function item(value: unknown): boolean {
  return record(value) && ['recoveryId', 'outboxId', 'requestId', 'tableSessionId', 'action'].every(key => id(value[key]))
    && isOutboxSequence(value.expectedBranchSequence) && isOutboxSequence(value.receivedBranchSequence) && BigInt(value.expectedBranchSequence) < BigInt(value.receivedBranchSequence)
    && isUtcTimestamp(value.requestedAt) && typeof value.hasLease === 'boolean' && Number.isSafeInteger(value.retryCount) && (value.retryCount as number) >= 0
    && ['nextRetryAt', 'leaseExpiresAt', 'lastFailedAt'].every(key => value[key] === null || isUtcTimestamp(value[key]))
    && value.hasLease === (value.leaseExpiresAt !== null)
    && (value.lastExpectedBranchSequence === null || isOutboxSequence(value.lastExpectedBranchSequence) && BigInt(value.lastExpectedBranchSequence) < BigInt(value.expectedBranchSequence))
}
export function parseCloudReplayPage(body: unknown, after?: string): CloudReplayPage {
  if (!record(body) || body.schemaVersion !== 1 || !isUtcTimestamp(body.observedAt) || !counts(body.summary)
    || !Array.isArray(body.items) || body.items.length > 50 || !body.items.every(item)) throw new Error('Invalid replay status response.')
  const summary = body.summary as unknown as CloudReplayPage['summary'], items = body.items as CloudReplayItem[]
  if (summary.pendingCount !== summary.leasedCount + summary.unleasedCount || summary.withRetriesCount > summary.pendingCount || summary.pendingCount < items.length
    || summary.leasedCount < items.filter(value => value.hasLease).length || summary.unleasedCount < items.filter(value => !value.hasLease).length
    || summary.withRetriesCount < items.filter(value => value.retryCount > 0).length
    || new Set(items.map(value => value.recoveryId)).size !== items.length
    || items.some((value, index) => BigInt(value.expectedBranchSequence) <= BigInt(index === 0 ? after ?? '0' : items[index - 1].expectedBranchSequence))
    || !(body.nextAfterSequence === null || isOutboxSequence(body.nextAfterSequence) && body.nextAfterSequence === items.at(-1)?.expectedBranchSequence)) throw new Error('Invalid replay status page.')
  return { schemaVersion: 1, observedAt: body.observedAt, summary: { pendingCount: summary.pendingCount, leasedCount: summary.leasedCount,
    unleasedCount: summary.unleasedCount, withRetriesCount: summary.withRetriesCount }, nextAfterSequence: body.nextAfterSequence as string | null,
    items: items.map(value => ({ recoveryId: value.recoveryId, outboxId: value.outboxId, expectedBranchSequence: value.expectedBranchSequence,
      receivedBranchSequence: value.receivedBranchSequence, requestId: value.requestId, tableSessionId: value.tableSessionId, action: value.action,
      requestedAt: value.requestedAt, hasLease: value.hasLease, retryCount: value.retryCount, nextRetryAt: value.nextRetryAt,
      leaseExpiresAt: value.leaseExpiresAt, lastFailedAt: value.lastFailedAt, lastExpectedBranchSequence: value.lastExpectedBranchSequence })) }
}
