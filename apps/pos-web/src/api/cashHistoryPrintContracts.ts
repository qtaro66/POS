import { isCashHistoryId } from './cashHistoryContracts.ts'
import { isProductionSnapshotId, type CashHistoryProductionItem } from './cashHistoryProductionContracts.ts'
import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface CashHistoryPrintQuery { limit?: number; cursor?: string }
export interface CashHistoryPrintJob { printJobId: string; requestId: string; snapshotId: string; productionStationId: string; status: 'PrintQueued' | 'Dispatching' | 'PrintDispatched' | 'Failed' | 'Unknown' | 'Completed'; attemptCount: number; createdAt: string; isReprint: boolean; originalPrintJobId: string | null }
export interface CashHistoryPrintAttempt { printAttemptId: string; attemptNumber: number; status: 'Connecting' | 'Dispatching' | 'PrintDispatched' | 'Failed' | 'Unknown'; automaticRetryAllowed: boolean; startedAt: string; firstByteAt: string | null; finishedAt: string | null }
interface Subject { schemaVersion: 1; branchId: string; asOf: string; cashRequestId: string; tableSessionId: string; snapshotId: string }
export interface CashHistoryPrintEvidence extends Subject { job: CashHistoryPrintJob | null }
export interface CashHistoryPrintAttemptsPage extends Subject { job: CashHistoryPrintJob; items: CashHistoryPrintAttempt[]; nextCursor: string | null }
const object = (v: unknown): v is Record<string, unknown> => v !== null && typeof v === 'object' && !Array.isArray(v)
const count = (v: unknown): v is number => Number.isInteger(v) && (v as number) >= 0 && (v as number) <= 2147483647
const cursor = (v: unknown): v is string => typeof v === 'string' && /^[A-Za-z0-9_-]{1,4096}$/.test(v)
const instant = (v: string) => v.replace(/\+00:00$/, 'Z').replace(/(?:\.(\d{1,7}))?Z$/, (_match, fraction: string | undefined) => '.' + (fraction ?? '').padEnd(7, '0') + 'Z')
function invalid(): never { throw new Error('Invalid recorded print evidence.') }
export function validateCashHistoryPrintQuery(q: CashHistoryPrintQuery) { if (q.limit !== undefined && (!count(q.limit) || q.limit < 1 || q.limit > 50) || q.cursor !== undefined && !cursor(q.cursor)) invalid() }
function subject(v: unknown, branch: string, request: string, snapshot: string, session?: string): Subject {
  if (!object(v) || v.schemaVersion !== 1 || v.branchId !== branch || v.cashRequestId !== request || v.snapshotId !== snapshot || !isProductionSnapshotId(v.snapshotId) || !isCashHistoryId(v.tableSessionId) || session !== undefined && v.tableSessionId !== session || !isUtcTimestamp(v.asOf)) invalid()
  return { schemaVersion: 1, branchId: branch, cashRequestId: request, snapshotId: snapshot, tableSessionId: v.tableSessionId, asOf: v.asOf }
}
function job(v: unknown, snapshot: string, original?: CashHistoryProductionItem): CashHistoryPrintJob {
  if (!object(v) || !isProductionSnapshotId(v.printJobId) || !isCashHistoryId(v.requestId) || v.snapshotId !== snapshot || !isCashHistoryId(v.productionStationId) || !['PrintQueued', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown', 'Completed'].includes(v.status as string) || !count(v.attemptCount) || !isUtcTimestamp(v.createdAt) || typeof v.isReprint !== 'boolean' || !(v.originalPrintJobId === null || isProductionSnapshotId(v.originalPrintJobId)) || v.isReprint !== false || v.originalPrintJobId !== null || v.printJobId === v.originalPrintJobId || original && (v.productionStationId !== original.productionStationId || !v.isReprint && v.requestId !== original.submittedRequestId)) invalid()
  return { printJobId: v.printJobId, requestId: v.requestId, snapshotId: snapshot, productionStationId: v.productionStationId, status: v.status as CashHistoryPrintJob['status'], attemptCount: v.attemptCount, createdAt: v.createdAt, isReprint: v.isReprint, originalPrintJobId: v.originalPrintJobId }
}
function page(v: unknown, q: CashHistoryPrintQuery): { items: unknown[]; nextCursor: string | null } {
  validateCashHistoryPrintQuery(q)
  if (!object(v) || !Array.isArray(v.items) || v.items.length > (q.limit ?? 20) || !(v.nextCursor === null || cursor(v.nextCursor)) || v.nextCursor !== null && v.items.length === 0) invalid()
  return { items: v.items, nextCursor: v.nextCursor }
}
export function parseCashHistoryPrintEvidence(v: unknown, branch: string, request: string, snapshot: string, original?: CashHistoryProductionItem): CashHistoryPrintEvidence {
  const s = subject(v, branch, request, snapshot, original?.tableSessionId)
  if (!object(v)) invalid()
  return { ...s, job: v.job === null ? null : job(v.job, snapshot, original) }
}
export const samePrintJobIdentity = (a: CashHistoryPrintJob, b: CashHistoryPrintJob) => a.printJobId === b.printJobId && a.requestId === b.requestId && a.snapshotId === b.snapshotId && a.productionStationId === b.productionStationId && instant(a.createdAt) === instant(b.createdAt) && a.isReprint === b.isReprint && a.originalPrintJobId === b.originalPrintJobId
export function parseCashHistoryPrintAttempts(v: unknown, branch: string, request: string, snapshot: string, jobId: string, q: CashHistoryPrintQuery, original?: CashHistoryProductionItem): CashHistoryPrintAttemptsPage {
  const s = subject(v, branch, request, snapshot, original?.tableSessionId), p = page(v, q)
  if (!object(v)) invalid()
  const currentJob = job(v.job, snapshot, original)
  if (currentJob.printJobId !== jobId) invalid()
  const items = p.items.map(raw => {
    if (!object(raw) || !isProductionSnapshotId(raw.printAttemptId) || !count(raw.attemptNumber) || raw.attemptNumber < 1 || raw.attemptNumber > currentJob.attemptCount || !['Connecting', 'Dispatching', 'PrintDispatched', 'Failed', 'Unknown'].includes(raw.status as string) || typeof raw.automaticRetryAllowed !== 'boolean' || !isUtcTimestamp(raw.startedAt) || !(raw.firstByteAt === null || isUtcTimestamp(raw.firstByteAt)) || !(raw.finishedAt === null || isUtcTimestamp(raw.finishedAt))) invalid()
    const terminal = ['PrintDispatched', 'Failed', 'Unknown'].includes(raw.status as string)
    if (terminal !== (raw.finishedAt !== null) || ['Dispatching', 'PrintDispatched'].includes(raw.status as string) && raw.firstByteAt === null || ['Connecting', 'Failed'].includes(raw.status as string) && raw.firstByteAt !== null || raw.automaticRetryAllowed && (raw.status !== 'Failed' || raw.firstByteAt !== null)) invalid()
    return { printAttemptId: raw.printAttemptId, attemptNumber: raw.attemptNumber, status: raw.status as CashHistoryPrintAttempt['status'], automaticRetryAllowed: raw.automaticRetryAllowed, startedAt: raw.startedAt, firstByteAt: raw.firstByteAt, finishedAt: raw.finishedAt }
  })
  if (new Set(items.map(i => i.printAttemptId)).size !== items.length || new Set(items.map(i => i.attemptNumber)).size !== items.length) invalid()
  return { ...s, job: currentJob, items, nextCursor: p.nextCursor }
}
