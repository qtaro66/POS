export interface PrintAttentionItem {
  readonly printJobId: string; readonly requestId: string; readonly tableSessionId: string; readonly snapshotId: string
  readonly purpose: 'BillPreview' | 'KitchenOrder'; readonly targetStation: string
  readonly status: 'PrintQueued' | 'Failed' | 'Unknown' | 'Dispatching'
  readonly createdAt: string; readonly assignedPrinterId: string | null; readonly nextRetryAt: string | null
  readonly attentionReason: 'AwaitingDispatch' | 'DispatchBlocked' | 'SendFailed' | 'SendOutcomeUnknown' | 'DispatchLeaseExpired'
  readonly latestAttemptStatus: 'Connecting' | 'Dispatching' | 'PrintDispatched' | 'Failed' | 'Unknown' | null
  readonly latestAttemptFinishedAt: string | null
}
export interface PrintAttentionPage {
  readonly schemaVersion: 1; readonly observedAt: string; readonly totalCount: number
  readonly items: readonly PrintAttentionItem[]; readonly nextCursor: string | null
}
export function isUtcTimestamp(value: unknown): value is string {
  return typeof value === 'string' && /^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?(?:Z|\+00:00)$/.test(value)
    && Number.isFinite(Date.parse(value)) && new Date(value).toISOString().slice(0, 19) === value.slice(0, 19)
}
export function isPrintAttentionCursor(value: unknown): value is string { return typeof value === 'string' && /^[A-Za-z0-9_-]{1,1024}$/.test(value) }
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }
function id(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && value.length <= 128 && value === value.trim() && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) }
function validItem(value: unknown): boolean {
  if (!record(value)) return false
  const reasons: Record<string, readonly string[]> = { PrintQueued: ['AwaitingDispatch', 'DispatchBlocked'], Failed: ['SendFailed'], Unknown: ['SendOutcomeUnknown'], Dispatching: ['DispatchLeaseExpired'] }
  return ['printJobId', 'requestId', 'tableSessionId', 'snapshotId', 'targetStation'].every(key => id(value[key]))
    && ['BillPreview', 'KitchenOrder'].includes(value.purpose as string)
    && Object.hasOwn(reasons, value.status as string) && reasons[value.status as string].includes(value.attentionReason as string)
    && isUtcTimestamp(value.createdAt) && (value.assignedPrinterId === null || id(value.assignedPrinterId))
    && (value.nextRetryAt === null || isUtcTimestamp(value.nextRetryAt))
    && (value.latestAttemptStatus === null || ['Connecting', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown'].includes(value.latestAttemptStatus as string))
    && (value.latestAttemptFinishedAt === null || isUtcTimestamp(value.latestAttemptFinishedAt))
}
export function parsePrintAttentionPage(body: unknown): PrintAttentionPage {
  if (!record(body) || body.schemaVersion !== 1 || !isUtcTimestamp(body.observedAt)
    || !Number.isSafeInteger(body.totalCount) || (body.totalCount as number) < 0
    || !Array.isArray(body.items) || body.items.length > 50 || !body.items.every(validItem)
    || (body.totalCount as number) < body.items.length || new Set(body.items.map(item => item.printJobId)).size !== body.items.length
    || !(body.nextCursor === null || isPrintAttentionCursor(body.nextCursor)) || body.nextCursor !== null && body.items.length === 0) throw new Error('Invalid print attention response.')
  // Project only public fields: internal diagnostics never reach view state.
  return { schemaVersion: 1, observedAt: body.observedAt, totalCount: body.totalCount as number, nextCursor: body.nextCursor,
    items: body.items.map(item => ({ printJobId: item.printJobId, requestId: item.requestId, tableSessionId: item.tableSessionId,
      snapshotId: item.snapshotId, purpose: item.purpose, targetStation: item.targetStation, status: item.status,
      createdAt: item.createdAt, assignedPrinterId: item.assignedPrinterId, nextRetryAt: item.nextRetryAt,
      attentionReason: item.attentionReason, latestAttemptStatus: item.latestAttemptStatus, latestAttemptFinishedAt: item.latestAttemptFinishedAt })) }
}
