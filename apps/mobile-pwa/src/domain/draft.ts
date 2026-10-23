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

export interface Draft extends DraftKey {
  /** Master Data version of the table session the lines were chosen from (D09). */
  readonly sessionMasterDataVersion: string
  readonly lines: readonly DraftLine[]
}

export function emptyDraft(key: DraftKey, sessionMasterDataVersion: string): Draft {
  return { ...key, sessionMasterDataVersion, lines: [] }
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
