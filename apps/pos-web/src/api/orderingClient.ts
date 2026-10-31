import type { ActiveTables, CommandResponse, MenuItem, OrderLine, OrderOption, SessionDetail, SessionMenu } from './orderingContracts.ts'

export interface OrderingSession {
  readonly serviceOrigin: string
  readonly branchId: string
  readonly deviceId: string
  readonly employeeId: string
  readonly deviceCredential: string
  readonly employeeSessionToken: string
}
export interface PreparedCommand { readonly requestId: string; readonly tableSessionId: string; readonly action: 'OpenTable' | 'SubmitOrder' }
type FailureReason = 'network' | 'timeout' | 'aborted' | 'protocol' | 'http'
export type CommandResult =
  | { readonly kind: 'durable'; readonly response: CommandResponse }
  | { readonly kind: 'blocked'; readonly requestId: string; readonly httpStatus: number }
  | { readonly kind: 'uncertain'; readonly requestId: string; readonly reason: FailureReason; readonly httpStatus?: number }
export type QueryResult<T> =
  | { readonly kind: 'ok'; readonly value: T }
  | { readonly kind: 'notFound' }
  | { readonly kind: 'unavailable'; readonly reason: FailureReason; readonly httpStatus?: number }
export type CommandQueryResult =
  | { readonly kind: 'durable'; readonly response: CommandResponse }
  | { readonly kind: 'noDurableResult'; readonly requestId: string }
  | { readonly kind: 'unavailable'; readonly reason: FailureReason; readonly httpStatus?: number }
interface ClientOptions {
  readonly fetch?: (url: string, init: RequestInit) => Promise<Response>
  readonly timeoutMs?: number
  readonly maxResponseBytes?: number
  readonly requestId?: () => string
  readonly now?: () => Date
}
type TransportResult = { readonly status: number; readonly body: unknown } | { readonly reason: FailureReason }
class ProtocolFailure extends Error {}
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }
function id(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && value.length <= 128 && value !== '.' && value !== '..' && value === value.trim() && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) === 127) }
function integer(value: unknown, minimum = 1): value is number { return Number.isSafeInteger(value) && (value as number) >= minimum }
function text(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 }
const statuses = new Set(['Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid', 'Completed', 'Cancelled', 'Closed'])
const activeStatuses = new Set(['Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid'])
function requireValid(condition: unknown): asserts condition { if (!condition) throw new ProtocolFailure('Invalid Branch ordering response.') }

function commandResponse(body: unknown, requestId: string, expected: 'BranchAccepted' | 'Rejected' | 'NoDurableResult', sessionId?: string): CommandResponse {
  requireValid(record(body) && record(body.result))
  const result = body.result
  requireValid(result.schemaVersion === 1 && result.requestId === requestId && result.status === expected)
  if (expected === 'NoDurableResult') {
    requireValid(['tableSessionId', 'tableSessionStatus', 'tableVersion', 'errorCode', 'serverReceivedAt', 'branchSequence'].every(key => result[key] === null))
    requireValid(body.validationDetails === null)
  } else {
    requireValid(id(result.tableSessionId) && (sessionId === undefined || result.tableSessionId === sessionId))
    requireValid(result.tableSessionStatus === null || statuses.has(result.tableSessionStatus as string))
    requireValid(result.tableVersion === null || integer(result.tableVersion))
    requireValid(typeof result.serverReceivedAt === 'string' && /(?:Z|\+00:00)$/.test(result.serverReceivedAt) && Number.isFinite(Date.parse(result.serverReceivedAt)))
    requireValid(result.branchSequence === null || integer(result.branchSequence))
    requireValid(expected === 'BranchAccepted'
      ? result.errorCode === null && integer(result.tableVersion) && statuses.has(result.tableSessionStatus as string)
      : text(result.errorCode))
  }
  return body as unknown as CommandResponse
}
function tables(body: unknown): ActiveTables {
  requireValid(record(body) && body.schemaVersion === 1 && body.includesAvailableTables === false && Array.isArray(body.sessions))
  const sessions = new Set<string>(), tableIds = new Set<string>()
  for (const session of body.sessions) {
    requireValid(record(session) && id(session.tableSessionId) && id(session.tableId) && integer(session.tableVersion) && activeStatuses.has(session.status as string))
    requireValid(!sessions.has(session.tableSessionId) && !tableIds.has(session.tableId))
    sessions.add(session.tableSessionId); tableIds.add(session.tableId)
  }
  return body as unknown as ActiveTables
}
function menuItem(value: unknown, expectedKind: string): asserts value is MenuItem {
  requireValid(record(value) && id(value.sellableItemId) && value.kind === expectedKind && text(value.displayName))
  requireValid(value.unitPrice === null || (typeof value.unitPrice === 'number' && Number.isFinite(value.unitPrice) && value.unitPrice >= 0))
  const availability = value.availability
  requireValid(record(availability) && ['existsInActivePackage', 'centralActive', 'branchEnabled', 'hasCurrentPrice', 'soldOut'].every(key => typeof availability[key] === 'boolean'))
  requireValid(Array.isArray(value.optionGroups))
  if (expectedKind !== 'Item') { requireValid(value.optionGroups.length === 0); return }
  const groups = new Set<string>()
  for (const group of value.optionGroups) {
    requireValid(record(group) && id(group.optionGroupId) && text(group.displayName) && !groups.has(group.optionGroupId))
    groups.add(group.optionGroupId)
    requireValid(group.optionType === 'SubItem' || group.optionType === 'Topping')
    requireValid(integer(group.minSelections, 0) && integer(group.maxSelections, 0) && group.maxSelections >= group.minSelections)
    requireValid(integer(group.maxQuantityPerOption) && typeof group.allowRepeatedOption === 'boolean' && Array.isArray(group.options))
    const options = new Set<string>()
    for (const option of group.options) {
      menuItem(option, group.optionType)
      requireValid(!options.has(option.sellableItemId)); options.add(option.sellableItemId)
    }
  }
}
function menu(body: unknown, sessionId: string): SessionMenu {
  requireValid(record(body) && body.schemaVersion === 1 && body.tableSessionId === sessionId && id(body.masterDataVersion) && Array.isArray(body.items))
  const ids = new Set<string>()
  for (const item of body.items) { menuItem(item, 'Item'); requireValid(!ids.has(item.sellableItemId)); ids.add(item.sellableItemId) }
  return body as unknown as SessionMenu
}

function utc(value: unknown): boolean { return typeof value === 'string' && /(?:Z|\+00:00)$/.test(value) && Number.isFinite(Date.parse(value)) }
function money(value: unknown): boolean { return typeof value === 'number' && Number.isFinite(value) && value >= 0 }
function detail(body: unknown, sessionId: string): SessionDetail {
  requireValid(record(body) && body.schemaVersion === 1 && record(body.session))
  requireValid(body.session.tableSessionId === sessionId && id(body.session.tableId) && activeStatuses.has(body.session.status as string) && integer(body.session.tableVersion))
  requireValid(utc(body.asOf) && typeof body.hasOpenCancellationRequest === 'boolean' && Array.isArray(body.batches))
  const batches = new Set<string>(), lines = new Set<string>()
  let hasActiveLines = false
  for (const batch of body.batches) {
    requireValid(record(batch) && text(batch.orderBatchId) && id(batch.requestId) && !batches.has(batch.orderBatchId))
    batches.add(batch.orderBatchId)
    requireValid((batch.branchSequence === null || integer(batch.branchSequence)) && utc(batch.serverReceivedAt) && batch.acceptanceStatus === 'BranchAccepted' && Array.isArray(batch.lines))
    requireValid([batch.activeGrossAmount, batch.activeLineDiscountAmount, batch.activeItemNetAmount].every(money))
    for (const line of batch.lines) {
      requireValid(record(line) && text(line.orderLineId) && !lines.has(line.orderLineId) && integer(line.lineSequence) && text(line.sellableItemId) && text(line.displayName))
      lines.add(line.orderLineId)
      requireValid(integer(line.originalQuantity) && integer(line.activeQuantity, 0) && line.activeQuantity <= line.originalQuantity)
      requireValid(line.status === 'Active' || line.status === 'Cancelled')
      requireValid(line.status !== 'Cancelled' || line.activeQuantity === 0)
      hasActiveLines ||= line.status === 'Active' && line.activeQuantity > 0
      requireValid(money(line.unitPrice) && (line.fulfillmentType === 'DineIn' || line.fulfillmentType === 'Takeaway') && (line.comment === null || typeof line.comment === 'string'))
      requireValid([line.activeGrossAmount, line.activeDiscountAmount, line.activeNetAmount].every(money) && Array.isArray(line.options))
      for (const option of line.options) {
        requireValid(record(option) && text(option.sellableItemId) && (option.optionGroupId === null || text(option.optionGroupId)) && text(option.displayName) && integer(option.quantityPerItem) && money(option.unitPrice))
      }
    }
  }
  const totals = body.billPreviewTotals
  requireValid(hasActiveLines ? record(totals) : totals === null)
  if (record(totals)) {
    requireValid(text(totals.masterDataVersion) && text(totals.calculationPolicyVersion))
    requireValid(['vatRate', 'serviceChargeRate', 'itemGrossAmount', 'lineDiscountAmount', 'itemNetAmount', 'billDiscountAmount', 'serviceChargeBase', 'serviceChargeAmount', 'totalBeforeRounding', 'netTotal', 'vatInclusiveAmount', 'amountBeforeVat'].every(key => money(totals[key])))
    requireValid(typeof totals.roundingAdjustment === 'number' && Number.isFinite(totals.roundingAdjustment))
  }
  return body as unknown as SessionDetail
}

/** Memory-only adapter. It never logs credentials, persists tokens, follows redirects, or retries a mutation. */
export function createOrderingClient(session: OrderingSession, options: ClientOptions = {}) {
  let origin: URL
  try { origin = new URL(session.serviceOrigin) } catch { throw new Error('Invalid Branch origin.') }
  if (origin.protocol !== 'https:' || origin.username || origin.password || origin.pathname !== '/' || origin.search || origin.hash) throw new Error('An HTTPS Branch origin is required.')
  if (![session.branchId, session.deviceId, session.employeeId].every(id) || ![session.deviceCredential, session.employeeSessionToken].every(value => typeof value === 'string' && /^[A-Za-z0-9._-]{1,2048}$/.test(value))) throw new Error('Invalid ordering session.')
  const identity = { branchId: session.branchId, deviceId: session.deviceId, employeeId: session.employeeId }
  const headers = { Accept: 'application/json', Authorization: `Device ${session.deviceCredential}`, 'X-POS-Employee-Session': session.employeeSessionToken }
  const prefix = `${origin.origin}/api/v1/ordering`
  const fetchImpl = options.fetch ?? globalThis.fetch.bind(globalThis)
  const timeoutMs = options.timeoutMs ?? 10_000, maxBytes = options.maxResponseBytes ?? 2 * 1024 * 1024
  if (!integer(timeoutMs) || timeoutMs > 2_147_483_647 || !integer(maxBytes)) throw new Error('Invalid ordering transport limits.')
  const requestIdFactory = options.requestId ?? (() => crypto.randomUUID())
  const now = options.now ?? (() => new Date())
  const prepared = new WeakMap<PreparedCommand, { path: string; body: string }>()
  const issuedIds = new Set<string>()

  async function request(path: string, method: 'GET' | 'POST', body?: string, callerSignal?: AbortSignal): Promise<TransportResult> {
    const controller = new AbortController()
    let timedOut = false
    const abort = () => controller.abort()
    if (callerSignal?.aborted) controller.abort()
    else callerSignal?.addEventListener('abort', abort, { once: true })
    const timer = setTimeout(() => { timedOut = true; controller.abort() }, timeoutMs)
    try {
      controller.signal.throwIfAborted()
      const response = await fetchImpl(prefix + path, { method, headers: { ...headers, ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) }, body, credentials: 'omit', cache: 'no-store', redirect: 'error', signal: controller.signal })
      if (![200, 404, 409].includes(response.status)) return { status: response.status, body: null }
      requireValid(/^(application\/json|application\/[a-z0-9.+-]+\+json)(;|$)/i.test(response.headers.get('Content-Type') ?? ''))
      const length = response.headers.get('Content-Length')
      requireValid(length === null || (Number.isSafeInteger(Number(length)) && Number(length) >= 0 && Number(length) <= maxBytes))
      requireValid(response.body !== null)
      const reader = response.body.getReader(), chunks: Uint8Array[] = []
      let total = 0
      try {
        for (;;) {
          const part = await reader.read()
          if (part.done) break
          total += part.value.byteLength
          requireValid(total <= maxBytes)
          chunks.push(part.value)
        }
      } finally { await reader.cancel().catch(() => {}); reader.releaseLock() }
      const bytes = new Uint8Array(total)
      let offset = 0
      for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength }
      controller.signal.throwIfAborted()
      try { return { status: response.status, body: JSON.parse(new TextDecoder('utf-8', { fatal: true }).decode(bytes)) as unknown } }
      catch { throw new ProtocolFailure('Invalid Branch ordering JSON.') }
    } catch (error) {
      const reason: FailureReason = timedOut ? 'timeout' : callerSignal?.aborted ? 'aborted' : error instanceof ProtocolFailure ? 'protocol' : 'network'
      return { reason }
    } finally { clearTimeout(timer); callerSignal?.removeEventListener('abort', abort) }
  }

  function prepare(action: PreparedCommand['action'], tableSessionId: string, payload: object): PreparedCommand {
    if (!id(tableSessionId)) throw new Error('Invalid table session identifier.')
    const requestId = requestIdFactory()
    if (!id(requestId) || issuedIds.has(requestId)) throw new Error('A new request identifier is required.')
    const command = Object.freeze({ action, requestId, tableSessionId })
    const body = JSON.stringify({ schemaVersion: 1, context: { requestId, ...identity, tableSessionId, clientCreatedAt: now().toISOString() }, ...payload })
    if (new TextEncoder().encode(body).byteLength > 64 * 1024) throw new Error('Ordering request exceeds 64 KiB.')
    issuedIds.add(requestId)
    prepared.set(command, { path: action === 'OpenTable' ? '/tables/open' : '/orders', body })
    return command
  }
  return {
    prepareOpenTable(tableId: string, proposedSessionId: string) {
      if (!id(tableId)) throw new Error('Invalid table identifier.')
      return prepare('OpenTable', proposedSessionId, { tableId })
    },
    prepareSubmitOrder(tableSessionId: string, lines: readonly OrderLine[]) {
      if (!Array.isArray(lines) || lines.length === 0) throw new Error('An order requires lines.')
      const snapshot = lines.map((line: OrderLine) => {
        if (!line || !id(line.sellableItemId) || !integer(line.quantity) || !['DineIn', 'Takeaway'].includes(line.fulfillmentType) || line.comment != null && typeof line.comment !== 'string' || line.options != null && !Array.isArray(line.options)) throw new Error('Invalid order line.')
        const opts = (line.options ?? []).map((option: OrderOption) => {
          if (!option || !id(option.optionGroupId) || !id(option.sellableItemId) || !integer(option.quantity)) throw new Error('Invalid order option.')
          return { optionGroupId: option.optionGroupId, sellableItemId: option.sellableItemId, quantity: option.quantity }
        })
        return { sellableItemId: line.sellableItemId, quantity: line.quantity, fulfillmentType: line.fulfillmentType, comment: line.comment ?? null, options: opts }
      })
      return prepare('SubmitOrder', tableSessionId, { lines: snapshot })
    },
    async execute(command: PreparedCommand, signal?: AbortSignal): Promise<CommandResult> {
      const saved = prepared.get(command)
      if (!saved) throw new Error('Command belongs to another ordering session.')
      const result = await request(saved.path, 'POST', saved.body, signal)
      if ('reason' in result) return { kind: 'uncertain', requestId: command.requestId, reason: result.reason }
      if ([400, 401, 403, 413].includes(result.status)) return { kind: 'blocked', requestId: command.requestId, httpStatus: result.status }
      if (result.status !== 200 && result.status !== 409) return { kind: 'uncertain', requestId: command.requestId, reason: 'http', httpStatus: result.status }
      try {
        return { kind: 'durable', response: commandResponse(result.body, command.requestId, result.status === 200 ? 'BranchAccepted' : 'Rejected', command.action === 'SubmitOrder' ? command.tableSessionId : undefined) }
      } catch { return { kind: 'uncertain', requestId: command.requestId, reason: 'protocol' } }
    },
    async commandStatus(requestId: string, signal?: AbortSignal): Promise<CommandQueryResult> {
      if (!id(requestId)) throw new Error('Invalid request identifier.')
      const result = await request(`/commands/${encodeURIComponent(requestId)}`, 'GET', undefined, signal)
      if ('reason' in result) return { kind: 'unavailable', reason: result.reason }
      if (result.status !== 200 && result.status !== 404) return { kind: 'unavailable', reason: 'http', httpStatus: result.status }
      try {
        const expected = result.status === 404 ? 'NoDurableResult' : record(result.body) && record(result.body.result) && result.body.result.status === 'Rejected' ? 'Rejected' : 'BranchAccepted'
        const response = commandResponse(result.body, requestId, expected)
        return expected === 'NoDurableResult' ? { kind: 'noDurableResult', requestId } : { kind: 'durable', response }
      } catch { return { kind: 'unavailable', reason: 'protocol' } }
    },
    async activeTables(signal?: AbortSignal): Promise<QueryResult<ActiveTables>> {
      const result = await request('/table-sessions', 'GET', undefined, signal)
      if ('reason' in result) return { kind: 'unavailable', reason: result.reason }
      if (result.status !== 200) return { kind: 'unavailable', reason: 'http', httpStatus: result.status }
      try { return { kind: 'ok', value: tables(result.body) } } catch { return { kind: 'unavailable', reason: 'protocol' } }
    },
    async sessionMenu(tableSessionId: string, signal?: AbortSignal): Promise<QueryResult<SessionMenu>> {
      if (!id(tableSessionId)) throw new Error('Invalid table session identifier.')
      const result = await request(`/table-sessions/${encodeURIComponent(tableSessionId)}/menu`, 'GET', undefined, signal)
      if ('reason' in result) return { kind: 'unavailable', reason: result.reason }
      if (result.status === 404) return { kind: 'notFound' }
      if (result.status !== 200) return { kind: 'unavailable', reason: 'http', httpStatus: result.status }
      try { return { kind: 'ok', value: menu(result.body, tableSessionId) } } catch { return { kind: 'unavailable', reason: 'protocol' } }
    },
    async sessionDetail(tableSessionId: string, signal?: AbortSignal): Promise<QueryResult<SessionDetail>> {
      if (!id(tableSessionId)) throw new Error('Invalid table session identifier.')
      const result = await request(`/table-sessions/${encodeURIComponent(tableSessionId)}`, 'GET', undefined, signal)
      if ('reason' in result) return { kind: 'unavailable', reason: result.reason }
      if (result.status === 404) return { kind: 'notFound' }
      if (result.status !== 200) return { kind: 'unavailable', reason: 'http', httpStatus: result.status }
      try { return { kind: 'ok', value: detail(result.body, tableSessionId) } } catch { return { kind: 'unavailable', reason: 'protocol' } }
    },
  }
}
