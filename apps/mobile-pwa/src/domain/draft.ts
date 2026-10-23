import { findItem, type Menu, type MenuItem } from './menu.ts'
import { itemProblems, type OptionSelection } from './optionRules.ts'

/**
 * Mobile Draft for one table session, shown to the user as the basket
 * (D18.6). Adding items, changing quantity and adding comments only edit the
 * Draft on this device; they are not Branch commands (D12.3, D12.4). A Draft
 * is never an order the Branch accepted.
 */
export interface DraftLine {
  /** Created on this device; identifies the line while it is edited. */
  readonly lineId: string
  readonly sellableItemId: string
  readonly quantity: number
  readonly selections: readonly OptionSelection[]
  /** Free text for the kitchen; not counted as an option selection (D08.4). */
  readonly comment: string
}

/** Identity a Draft is bound to (D10.5). */
export interface DraftKey {
  readonly deviceId: string
  readonly employeeId: string
  readonly tableSessionId: string
}

/**
 * `ExpiredNeedsReview` is set once an unsent Draft is older than the
 * retention period (D19.4, NFR-06). It is never cleared automatically, so
 * moving the device clock back does not revive it. What happens next
 * (deletion workflow) is pending decision DEC-6; nothing is deleted.
 */
export type DraftReviewState = 'Active' | 'ExpiredNeedsReview'

export interface Draft extends DraftKey {
  /** Master Data version of the table session the lines were chosen from (D09). */
  readonly sessionMasterDataVersion: string
  readonly lines: readonly DraftLine[]
  /** Device time the Draft was started (ISO 8601 UTC); reference only (D12.9). */
  readonly createdAt: string
  /** Device time of the last change (ISO 8601 UTC). */
  readonly updatedAt: string
  readonly reviewState: DraftReviewState
}

/** Unsent Drafts are kept 30 days before they need review (D19.4, NFR-06). */
export const DRAFT_RETENTION_DAYS = 30
const DAY_MS = 24 * 60 * 60 * 1000

export function emptyDraft(key: DraftKey, sessionMasterDataVersion: string, now: Date): Draft {
  const at = now.toISOString()
  return {
    ...key,
    sessionMasterDataVersion,
    lines: [],
    createdAt: at,
    updatedAt: at,
    reviewState: 'Active',
  }
}

/** Records that the Draft was changed at `now`. */
export function touchDraft(draft: Draft, now: Date): Draft {
  return { ...draft, updatedAt: now.toISOString() }
}

/**
 * Marks a Draft `ExpiredNeedsReview` once 30 days have passed since it was
 * started. The period is counted from `createdAt`, the literal reading of
 * "kept 30 days" in D19.4; the device clock is the only one available
 * offline. Returns the same object when nothing changes.
 */
export function applyDraftExpiry(draft: Draft, now: Date): Draft {
  if (draft.reviewState === 'ExpiredNeedsReview') return draft
  const started = Date.parse(draft.createdAt)
  const expired = Number.isNaN(started) || now.getTime() - started >= DRAFT_RETENTION_DAYS * DAY_MS
  return expired ? { ...draft, reviewState: 'ExpiredNeedsReview' } : draft
}

export function addLine(draft: Draft, line: DraftLine): Draft {
  return { ...draft, lines: [...draft.lines, line] }
}

export function replaceLine(draft: Draft, line: DraftLine): Draft {
  return {
    ...draft,
    lines: draft.lines.map((existing) => (existing.lineId === line.lineId ? line : existing)),
  }
}

export function removeLine(draft: Draft, lineId: string): Draft {
  return { ...draft, lines: draft.lines.filter((line) => line.lineId !== lineId) }
}

/** Quantity of a line; at least 1. Removing a line is a separate action. */
export function setLineQuantity(draft: Draft, lineId: string, quantity: number): Draft {
  const next = Math.max(1, Math.trunc(quantity))
  return {
    ...draft,
    lines: draft.lines.map((line) => (line.lineId === lineId ? { ...line, quantity: next } : line)),
  }
}

/** Number of items in the lines, counted by quantity. */
export function countLineItems(lines: readonly DraftLine[]): number {
  return lines.reduce((count, line) => count + line.quantity, 0)
}

export function countItems(draft: Draft): number {
  return countLineItems(draft.lines)
}

export type LineProblem = 'itemUnavailable' | 'optionsInvalid'

/**
 * Why a basket line cannot be sent as it is, judged against the current menu.
 * The Branch Service checks again on submit and rejects the whole batch if any
 * line fails (D08.4, D09); this only warns early.
 */
export function lineProblem(menu: Menu, line: DraftLine): LineProblem | null {
  const item: MenuItem | undefined = findItem(menu, line.sellableItemId)
  if (!item || item.availability !== 'available') return 'itemUnavailable'
  return itemProblems(item, line.selections).size > 0 ? 'optionsInvalid' : null
}
