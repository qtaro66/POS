import type { createOrderingClient, CommandQueryResult, CommandResult } from '../api/orderingClient.ts'
import type { CommandResponse, MenuItem, SessionMenu } from '../api/orderingContracts.ts'
import { createBasketStore, type BasketRecord, type BasketVersion } from './basketStore.ts'
import { createCommandJournal } from './commandJournal.ts'
import { createCommandRecovery, type RecoveryOutcome } from './commandRecovery.ts'

type Client = ReturnType<typeof createOrderingClient>
type BlockReason = 'busy' | 'cancelled' | 'storageUnavailable' | 'staleBasket' | 'missingBasket' | 'emptyBasket' | 'sessionUnavailable' | 'sessionLocked' | 'catalogUnavailable' | 'catalogChanged' | 'selectionUnavailable' | 'invalidSelection' | 'localWriteFailed' | 'invalidRequest'
export type BasketSubmissionResult =
  | { readonly kind: 'blocked'; readonly reason: BlockReason; readonly requestId?: string }
  | { readonly kind: 'pending'; readonly requestId: string; readonly result?: CommandQueryResult | CommandResult }
  | { readonly kind: 'durable'; readonly requestId: string; readonly response: CommandResponse; readonly localApplied: boolean }

const available = (item: MenuItem) => item.unitPrice !== null && item.availability.existsInActivePackage && item.availability.centralActive && item.availability.branchEnabled && item.availability.hasCurrentPrice && !item.availability.soldOut

/** Advisory checks mirror CatalogSelectionValidator; Branch still decides at commit. */
export function basketSelectionProblem(basket: Pick<BasketRecord, 'lines'>, menu: SessionMenu): BlockReason | undefined {
  for (const line of basket.lines) {
    if (line.quantity > 2_147_483_647 || (line.options ?? []).some(option => option.quantity > 2_147_483_647)) return 'invalidSelection'
    const item = menu.items.find(item => item.sellableItemId === line.sellableItemId)
    if (!item || !available(item)) return 'selectionUnavailable'
    const selected = new Set<string>()
    for (const option of line.options ?? []) {
      const group = item.optionGroups.find(group => group.optionGroupId === option.optionGroupId)
      const value = group?.options.find(value => value.sellableItemId === option.sellableItemId)
      const key = JSON.stringify([option.optionGroupId, option.sellableItemId])
      if (!group || !value || selected.has(key) || option.quantity > (group.allowRepeatedOption ? group.maxQuantityPerOption : 1)) return 'invalidSelection'
      if (!available(value)) return 'selectionUnavailable'
      selected.add(key)
    }
    for (const group of item.optionGroups) {
      const count = (line.options ?? []).filter(option => option.optionGroupId === group.optionGroupId).reduce((sum, option) => sum + BigInt(option.quantity), 0n)
      if (count < BigInt(group.minSelections) || count > BigInt(group.maxSelections)) return 'invalidSelection'
    }
  }
}

/** No I/O on construction; each submission/retry is explicitly requested by its caller. */
export function createBasketSubmission(client: Client, factory: IDBFactory | undefined = globalThis.indexedDB) {
  const baskets = createBasketStore(factory), journal = createCommandJournal(factory)
  const recovery = createCommandRecovery(client, journal), active = new Set<string>()
  const owner = Object.freeze({ ...client.scope, employeeId: client.employeeId })
  const scope = (tableSessionId: string) => ({ ...owner, tableSessionId })

  async function preflight(basket: BasketRecord, signal?: AbortSignal): Promise<BlockReason | undefined> {
    if (signal?.aborted) return 'cancelled'
    const detail = await client.sessionDetail(basket.tableSessionId, signal)
    if (signal?.aborted) return 'cancelled'
    if (detail.kind !== 'ok') return 'sessionUnavailable'
    if (detail.value.session.status !== 'Ordering') return 'sessionLocked'
    const menu = await client.sessionMenu(basket.tableSessionId, signal)
    if (signal?.aborted) return 'cancelled'
    if (menu.kind !== 'ok') return 'catalogUnavailable'
    if (menu.value.masterDataVersion !== basket.sessionMasterDataVersion) return 'catalogChanged'
    return basketSelectionProblem(basket, menu.value)
  }

  async function finish(basket: BasketRecord, outcome: RecoveryOutcome<CommandQueryResult | CommandResult>): Promise<BasketSubmissionResult> {
    const requestId = basket.pendingRequestId!
    if (outcome.result.kind !== 'durable') return { kind: 'pending', requestId, result: outcome.result }
    let localApplied = false
    if (outcome.recoveryRecorded) {
      try { await baskets.apply(scope(basket.tableSessionId), basket, outcome.result.response); localApplied = true } catch { /* Preserve the durable Branch outcome even if local application fails. */ }
    }
    return { kind: 'durable', requestId, response: outcome.result.response, localApplied }
  }

  async function run(tableSessionId: string, expected: BasketVersion, action: 'submit' | 'recover' | 'retry', signal?: AbortSignal): Promise<BasketSubmissionResult> {
    // Copy the token before awaiting, so a caller cannot change the target revision in flight.
    const version = { basketId: expected?.basketId, revision: expected?.revision }
    let currentRequestId: string | undefined
    if (active.has(tableSessionId)) return { kind: 'blocked', reason: 'busy' }
    active.add(tableSessionId)
    try {
      if (signal?.aborted) return { kind: 'blocked', reason: 'cancelled' }
      let basket: BasketRecord | null
      try { basket = await baskets.load(scope(tableSessionId)) } catch { return { kind: 'blocked', reason: 'storageUnavailable' } }
      if (!basket) return { kind: 'blocked', reason: 'missingBasket' }
      if (basket.basketId !== version.basketId || basket.revision !== version.revision) return { kind: 'blocked', reason: 'staleBasket' }
      if (basket.pendingRequestId) {
        const requestId = basket.pendingRequestId
        currentRequestId = requestId
        if (action === 'submit') return { kind: 'pending', requestId }
        const checked = await recovery.recover(requestId, signal)
        if (checked.result.kind !== 'noDurableResult' || action === 'recover') return await finish(basket, checked)
        const entry = await journal.load(client.scope, requestId)
        if (!entry || entry.state === 'resolved') return { kind: 'pending', requestId, result: checked.result }
        const reason = await preflight(basket, signal)
        if (reason) return { kind: 'blocked', reason, requestId }
        return await finish(basket, await recovery.retry(requestId, signal))
      }
      if (action !== 'submit') return { kind: 'blocked', reason: 'missingBasket' }
      if (basket.lines.length === 0) return { kind: 'blocked', reason: 'emptyBasket' }
      const reason = await preflight(basket, signal)
      if (reason) return { kind: 'blocked', reason }
      let command
      try { command = client.snapshot(client.prepareSubmitOrder(tableSessionId, basket.lines)) } catch { return { kind: 'blocked', reason: 'invalidRequest' } }
      try { basket = await baskets.stage(scope(tableSessionId), version, command) } catch { return { kind: 'blocked', reason: 'localWriteFailed' } }
      currentRequestId = command.requestId
      // Even cancellation after staging retains the frozen original request for recovery.
      if (signal?.aborted) return { kind: 'pending', requestId: command.requestId }
      return await finish(basket, await recovery.retry(command.requestId, signal))
    } catch {
      // A storage/adapter exception is never a durable rejection and never permits a replacement ID.
      return { kind: 'blocked', reason: 'storageUnavailable', ...(currentRequestId ? { requestId: currentRequestId } : {}) }
    } finally { active.delete(tableSessionId) }
  }
  return {
    load: (tableSessionId: string) => baskets.load(scope(tableSessionId)),
    submit: (tableSessionId: string, expected: BasketVersion, signal?: AbortSignal) => run(tableSessionId, expected, 'submit', signal),
    recover: (tableSessionId: string, expected: BasketVersion, signal?: AbortSignal) => run(tableSessionId, expected, 'recover', signal),
    retry: (tableSessionId: string, expected: BasketVersion, signal?: AbortSignal) => run(tableSessionId, expected, 'retry', signal),
  }
}
