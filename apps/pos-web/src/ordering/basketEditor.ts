import { snapshotOrderLines, type createOrderingClient } from '../api/orderingClient.ts'
import type { FulfillmentType, OrderLine, SessionDetail, SessionMenu } from '../api/orderingContracts.ts'
import { createBasketStore, type BasketRecord, type BasketVersion } from './basketStore.ts'
import { basketSelectionProblem, createBasketSubmission, type BasketSubmissionResult } from './basketSubmission.ts'

type Client = ReturnType<typeof createOrderingClient>
type EditorIssue = 'storageUnavailable' | 'sessionUnavailable' | 'sessionLocked' | 'catalogUnavailable' | 'catalogChanged' | 'localWriteFailed' | 'refreshRequired'
export interface BasketEditorState {
  readonly phase: 'idle' | 'loading' | 'ready'
  readonly activity: 'idle' | 'saving' | 'submitting' | 'recovering' | 'retrying'
  readonly basket: BasketRecord | null
  readonly detail: SessionDetail | null
  readonly menu: SessionMenu | null
  readonly issue: EditorIssue | null
  readonly editError: 'invalidLine' | 'selectionUnavailable' | null
  readonly newFulfillment: FulfillmentType
  readonly outcome: BasketSubmissionResult | null
}

/** Device-persistent editor state. No optimistic basket edits or network calls at construction. */
export function createBasketEditor(client: Client, tableSessionId: string, factory: IDBFactory | undefined = globalThis.indexedDB) {
  const baskets = createBasketStore(factory), submission = createBasketSubmission(client, factory)
  const scope = Object.freeze({ ...client.scope, employeeId: client.employeeId, tableSessionId })
  let state: BasketEditorState = { phase: 'idle', activity: 'idle', basket: null, detail: null, menu: null, issue: null, editError: null, newFulfillment: 'DineIn', outcome: null }
  const listeners = new Set<() => void>()
  let epoch = 0, abort: AbortController | undefined
  const update = (patch: Partial<BasketEditorState>) => { state = { ...state, ...patch }; for (const listener of listeners) listener() }
  function cancel() { epoch++; abort?.abort() }
  function editable() { return state.phase === 'ready' && state.activity === 'idle' && state.issue === null && state.menu !== null && !state.basket?.pendingRequestId }

  async function refresh() {
    cancel()
    const current = epoch, controller = new AbortController()
    abort = controller
    update({ phase: 'loading', activity: 'idle', basket: null, menu: null, detail: null, issue: null, editError: null, outcome: null })
    let basket: BasketRecord | null
    try { basket = await baskets.load(scope) }
    catch { if (current === epoch) update({ phase: 'ready', issue: 'storageUnavailable' }); return }
    if (current !== epoch) return
    update({ basket })
    const reads = await Promise.allSettled([client.sessionDetail(tableSessionId, controller.signal), client.sessionMenu(tableSessionId, controller.signal)])
    if (current !== epoch || controller.signal.aborted) return
    const detail = reads[0].status === 'fulfilled' && reads[0].value.kind === 'ok' ? reads[0].value.value : null
    const menu = reads[1].status === 'fulfilled' && reads[1].value.kind === 'ok' ? reads[1].value.value : null
    const issue: EditorIssue | null = !detail ? 'sessionUnavailable' : detail.session.status !== 'Ordering' ? 'sessionLocked'
      : !menu ? 'catalogUnavailable' : basket && basket.sessionMasterDataVersion !== menu.masterDataVersion ? 'catalogChanged' : null
    update({ phase: 'ready', detail, menu, issue })
  }

  async function persist(lines: readonly OrderLine[], validateLine?: OrderLine): Promise<boolean> {
    if (!editable()) return false
    let snapshot: OrderLine[]
    try { snapshot = lines.length ? snapshotOrderLines(lines) : [] }
    catch { update({ editError: 'invalidLine' }); return false }
    if (validateLine && basketSelectionProblem({ lines: [validateLine] }, state.menu!)) { update({ editError: 'selectionUnavailable' }); return false }
    const current = epoch, expected = state.basket, version = state.basket?.sessionMasterDataVersion ?? state.menu!.masterDataVersion
    update({ activity: 'saving', editError: null, outcome: null })
    try {
      const saved = await baskets.save({ ...scope, sessionMasterDataVersion: version, lines: snapshot }, expected)
      if (current === epoch) update({ basket: saved, activity: 'idle' })
      return true
    } catch {
      // Retain the last committed view; require a reload instead of overwriting another tab.
      if (current === epoch) update({ activity: 'idle', issue: 'localWriteFailed' })
      return false
    }
  }

  async function command(action: 'submit' | 'recover' | 'retry'): Promise<BasketSubmissionResult | null> {
    const basket = state.basket
    if (state.activity !== 'idle' || state.phase !== 'ready' || !basket) return null
    if (action === 'submit' ? !editable() : !basket.pendingRequestId) return null
    const current = epoch, controller = new AbortController()
    abort = controller
    update({ activity: action === 'submit' ? 'submitting' : action === 'recover' ? 'recovering' : 'retrying', editError: null, outcome: null })
    const outcome = await submission[action](tableSessionId, basket, controller.signal)
    let saved: BasketRecord | null
    try { saved = await baskets.load(scope) }
    catch {
      if (current === epoch) update({ activity: 'idle', outcome, issue: 'storageUnavailable' })
      return outcome
    }
    if (current === epoch) update({ activity: 'idle', basket: saved, outcome, issue: 'refreshRequired' })
    return outcome
  }

  return {
    getSnapshot: () => state,
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    refresh, cancel,
    setNewFulfillment(type: FulfillmentType) { if (type === 'DineIn' || type === 'Takeaway') update({ newFulfillment: type }) },
    addLine(input: Omit<OrderLine, 'fulfillmentType'>) {
      const line = { ...input, fulfillmentType: state.newFulfillment }
      return persist([...(state.basket?.lines ?? []), line], line)
    },
    replaceLine(index: number, line: OrderLine, expected?: BasketVersion) {
      if (expected && (!state.basket || expected.basketId !== state.basket.basketId || expected.revision !== state.basket.revision)) return Promise.resolve(false)
      if (!Number.isSafeInteger(index) || index < 0 || index >= (state.basket?.lines.length ?? 0)) return Promise.resolve(false)
      return persist(state.basket!.lines.map((saved, position) => position === index ? line : saved), line)
    },
    removeLine(index: number) {
      if (!Number.isSafeInteger(index) || index < 0 || index >= (state.basket?.lines.length ?? 0)) return Promise.resolve(false)
      return persist(state.basket!.lines.filter((_saved, position) => position !== index))
    },
    submit: () => command('submit'), recover: () => command('recover'), retry: () => command('retry'),
  }
}
