import type { DraftLine } from './draft.ts'

/**
 * A Mobile request held in the persistent local outbox (D20.1, 3.2):
 *
 *   Draft → Pending → Submitted ─┬→ BranchAccepted
 *                                ├→ Rejected
 *                                └→ Unknown ──status query──→ BranchAccepted / Rejected / Pending
 *
 * The Draft itself is the basket (`draft.ts`); it becomes a `Pending` request
 * when the user sends it. The `requestId` is created once at that moment and
 * is reused for every retry and status query, so the Branch Service never
 * receives the same order twice under different identifiers.
 */
export type MobileRequestState = 'Pending' | 'Submitted' | 'BranchAccepted' | 'Rejected' | 'Unknown'

/** Commands Mobile may send (D12.4). Only `SubmitOrder` is held in the outbox so far. */
export type MobileCommand = 'SubmitOrder'

export interface MobileRequest {
  readonly requestId: string
  readonly command: MobileCommand
  readonly deviceId: string
  /** Employee who sent it; never changes once sending has started (D10.5). */
  readonly employeeId: string
  readonly tableSessionId: string
  readonly sessionMasterDataVersion: string
  /** The Draft lines as they were when the user sent them; kept if rejected (D20.1). */
  readonly lines: readonly DraftLine[]
  readonly state: MobileRequestState
  /** Device time the user sent it (`clientCreatedAt`, reference only, D12.9). */
  readonly clientCreatedAt: string
  /** Device time of the last state change. */
  readonly updatedAt: string
}

export type MobileRequestEvent =
  /** Sending has started (a request was written to the network). */
  | { readonly type: 'sendStarted' }
  /** The Branch Service committed the command (`BranchAccepted`). */
  | { readonly type: 'accepted' }
  /** The Branch Service refused the command explicitly. */
  | { readonly type: 'rejected' }
  /** No result arrived after sending started (timeout, lost connection, app closed). */
  | { readonly type: 'resultUnknown' }
  /** A status query found no durable result for the `requestId`, so it may be sent again. */
  | { readonly type: 'notFound' }

const transitions: Record<MobileRequestState, Partial<Record<MobileRequestEvent['type'], MobileRequestState>>> = {
  Pending: { sendStarted: 'Submitted' },
  Submitted: { accepted: 'BranchAccepted', rejected: 'Rejected', resultUnknown: 'Unknown' },
  Unknown: { accepted: 'BranchAccepted', rejected: 'Rejected', notFound: 'Pending' },
  BranchAccepted: {},
  Rejected: {},
}

export class InvalidRequestTransitionError extends Error {
  constructor(state: MobileRequestState, event: MobileRequestEvent['type']) {
    super(`A ${state} request cannot handle ${event}.`)
    this.name = 'InvalidRequestTransitionError'
  }
}

/**
 * Applies one D20.1 transition. Anything else, for example resending a
 * `BranchAccepted` request or treating `Unknown` as failed, is refused.
 */
export function transitionRequest(
  request: MobileRequest,
  event: MobileRequestEvent,
  now: Date,
): MobileRequest {
  const next = transitions[request.state][event.type]
  if (!next) throw new InvalidRequestTransitionError(request.state, event.type)
  return { ...request, state: next, updatedAt: now.toISOString() }
}

/** Requests the Branch Service has not yet accepted or rejected; they never expire (D19.4). */
export function isOutstanding(request: MobileRequest): boolean {
  return request.state !== 'BranchAccepted' && request.state !== 'Rejected'
}

export function newSubmitOrderRequest(
  requestId: string,
  draft: {
    readonly deviceId: string
    readonly employeeId: string
    readonly tableSessionId: string
    readonly sessionMasterDataVersion: string
    readonly lines: readonly DraftLine[]
  },
  now: Date,
): MobileRequest {
  const at = now.toISOString()
  return {
    requestId,
    command: 'SubmitOrder',
    deviceId: draft.deviceId,
    employeeId: draft.employeeId,
    tableSessionId: draft.tableSessionId,
    sessionMasterDataVersion: draft.sessionMasterDataVersion,
    lines: draft.lines,
    state: 'Pending',
    clientCreatedAt: at,
    updatedAt: at,
  }
}
