import { describe, expect, it } from 'vitest'
import { addLine, emptyDraft } from './draft.ts'
import {
  InvalidRequestTransitionError,
  isOutstanding,
  newSubmitOrderRequest,
  transitionRequest,
  type MobileRequest,
  type MobileRequestEvent,
  type MobileRequestState,
} from './mobileRequest.ts'
import { summarizeOutbox } from './outbox.ts'

const NOW = new Date('2026-01-15T03:00:00.000Z')
const LATER = new Date('2026-01-15T03:00:05.000Z')
const key = { deviceId: 'device-1', employeeId: 'employee-1', tableSessionId: 'session-1' }
const draft = addLine(emptyDraft(key, 'md-1', NOW), {
  lineId: 'line-1',
  sellableItemId: 'fixture-item-water',
  quantity: 1,
  selections: [],
  comment: '',
})
const pending = newSubmitOrderRequest('request-1', draft, NOW)

function inState(state: MobileRequestState): MobileRequest {
  return { ...pending, state }
}

describe('newSubmitOrderRequest', () => {
  it('starts Pending with the Draft identity, lines and one requestId (3.2, D10.5)', () => {
    expect(pending).toEqual({
      requestId: 'request-1',
      command: 'SubmitOrder',
      deviceId: 'device-1',
      employeeId: 'employee-1',
      tableSessionId: 'session-1',
      sessionMasterDataVersion: 'md-1',
      lines: draft.lines,
      state: 'Pending',
      clientCreatedAt: NOW.toISOString(),
      updatedAt: NOW.toISOString(),
    })
  })
})

describe('transitionRequest (D20.1)', () => {
  it.each<[MobileRequestState, MobileRequestEvent['type'], MobileRequestState]>([
    ['Pending', 'sendStarted', 'Submitted'],
    ['Submitted', 'accepted', 'BranchAccepted'],
    ['Submitted', 'rejected', 'Rejected'],
    ['Submitted', 'resultUnknown', 'Unknown'],
    ['Unknown', 'accepted', 'BranchAccepted'],
    ['Unknown', 'rejected', 'Rejected'],
    ['Unknown', 'notFound', 'Pending'],
  ])('%s + %s → %s', (from, event, to) => {
    const next = transitionRequest(inState(from), { type: event }, LATER)
    expect(next.state).toBe(to)
    expect(next.requestId).toBe('request-1')
    expect(next.updatedAt).toBe(LATER.toISOString())
  })

  it.each<[MobileRequestState, MobileRequestEvent['type']]>([
    ['Pending', 'accepted'],
    ['Pending', 'resultUnknown'],
    ['Submitted', 'sendStarted'],
    ['Submitted', 'notFound'],
    ['Unknown', 'sendStarted'],
    ['BranchAccepted', 'sendStarted'],
    ['BranchAccepted', 'rejected'],
    ['Rejected', 'sendStarted'],
    ['Rejected', 'accepted'],
  ])('refuses %s + %s', (from, event) => {
    expect(() => transitionRequest(inState(from), { type: event }, LATER)).toThrow(
      InvalidRequestTransitionError,
    )
  })

  it('keeps the lines of a rejected request so the Draft is not lost', () => {
    const rejected = transitionRequest(inState('Submitted'), { type: 'rejected' }, LATER)
    expect(rejected.lines).toEqual(draft.lines)
  })
})

describe('outbox counts', () => {
  it('treats only accepted and rejected requests as settled', () => {
    expect(
      (['Pending', 'Submitted', 'Unknown', 'BranchAccepted', 'Rejected'] as const).map((state) =>
        isOutstanding(inState(state)),
      ),
    ).toEqual([true, true, true, false, false])
  })

  it('counts each non-empty Draft once and outstanding requests by state', () => {
    const empty = emptyDraft({ ...key, tableSessionId: 'session-2' }, 'md-1', NOW)
    const requests = [
      inState('Pending'),
      { ...inState('Submitted'), requestId: 'r2' },
      { ...inState('Unknown'), requestId: 'r3' },
      { ...inState('Unknown'), requestId: 'r4' },
      { ...inState('BranchAccepted'), requestId: 'r5' },
      { ...inState('Rejected'), requestId: 'r6' },
    ]
    expect(summarizeOutbox([draft, empty], requests)).toEqual({
      draft: 1,
      pending: 1,
      submitted: 1,
      unknown: 2,
    })
  })
})
