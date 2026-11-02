import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface CloudOutboxItem {
  readonly outboxId: string; readonly branchSequence: string; readonly requestId: string; readonly tableSessionId: string; readonly action: string
  readonly processingStatus: 'Pending' | 'Processing' | 'Failed' | 'Other'; readonly createdAt: string; readonly retryCount: number
  readonly nextRetryAt: string | null; readonly leaseExpiresAt: string | null; readonly lastFailedAt: string | null; readonly expectedBranchSequence: string | null
}
export interface CloudOutboxPage {
  readonly schemaVersion: 1; readonly observedAt: string
  readonly summary: { readonly unacknowledgedCount: number; readonly pendingCount: number; readonly processingCount: number; readonly failedCount: number; readonly otherCount: number }
  readonly items: readonly CloudOutboxItem[]; readonly nextAfterSequence: string | null
}
export function isOutboxSequence(value: unknown): value is string { return typeof value === 'string' && /^[1-9][0-9]{0,18}$/.test(value) && BigInt(value) <= 9223372036854775807n }
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }
function id(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && value.length <= 128 && value === value.trim() && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) }
function counts(value: unknown): boolean { return record(value) && ['unacknowledgedCount', 'pendingCount', 'processingCount', 'failedCount', 'otherCount'].every(key => Number.isSafeInteger(value[key]) && (value[key] as number) >= 0) }
function item(value: unknown): boolean {
  return record(value) && ['outboxId', 'requestId', 'tableSessionId', 'action'].every(key => id(value[key])) && isOutboxSequence(value.branchSequence)
    && ['Pending', 'Processing', 'Failed', 'Other'].includes(value.processingStatus as string) && isUtcTimestamp(value.createdAt)
    && Number.isSafeInteger(value.retryCount) && (value.retryCount as number) >= 0
    && ['nextRetryAt', 'leaseExpiresAt', 'lastFailedAt'].every(key => value[key] === null || isUtcTimestamp(value[key]))
    && (value.expectedBranchSequence === null || isOutboxSequence(value.expectedBranchSequence) && BigInt(value.expectedBranchSequence) < BigInt(value.branchSequence))
}
export function parseCloudOutboxPage(body: unknown, after?: string): CloudOutboxPage {
  if (!record(body) || body.schemaVersion !== 1 || !isUtcTimestamp(body.observedAt) || !counts(body.summary)
    || !Array.isArray(body.items) || body.items.length > 50 || !body.items.every(item)) throw new Error('Invalid outbox status response.')
  const summary = body.summary as unknown as CloudOutboxPage['summary'], items = body.items as CloudOutboxItem[]
  if (summary.unacknowledgedCount !== summary.pendingCount + summary.processingCount + summary.failedCount + summary.otherCount
    || summary.unacknowledgedCount < items.length || new Set(items.map(value => value.outboxId)).size !== items.length
    || items.some((value, index) => BigInt(value.branchSequence) <= BigInt(index === 0 ? after ?? '0' : items[index - 1].branchSequence))
    || !(body.nextAfterSequence === null || isOutboxSequence(body.nextAfterSequence) && body.nextAfterSequence === items.at(-1)?.branchSequence)) throw new Error('Invalid outbox status page.')
  return { schemaVersion: 1, observedAt: body.observedAt, summary: { unacknowledgedCount: summary.unacknowledgedCount,
    pendingCount: summary.pendingCount, processingCount: summary.processingCount, failedCount: summary.failedCount, otherCount: summary.otherCount },
    nextAfterSequence: body.nextAfterSequence as string | null, items: items.map(value => ({ outboxId: value.outboxId, branchSequence: value.branchSequence,
      requestId: value.requestId, tableSessionId: value.tableSessionId, action: value.action, processingStatus: value.processingStatus,
      createdAt: value.createdAt, retryCount: value.retryCount, nextRetryAt: value.nextRetryAt, leaseExpiresAt: value.leaseExpiresAt,
      lastFailedAt: value.lastFailedAt, expectedBranchSequence: value.expectedBranchSequence })) }
}
