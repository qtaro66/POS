import { describe, expect, it } from 'vitest'
import { FIXTURE_MENU, FIXTURE_PROTEIN_GROUP } from '../adapters/fixtureMenu.ts'
import {
  addLine,
  countItems,
  emptyDraft,
  lineProblem,
  removeLine,
  replaceLine,
  setLineQuantity,
  type DraftLine,
} from './draft.ts'

const key = { deviceId: 'device-1', employeeId: 'employee-1', tableSessionId: 'session-1' }

function line(lineId: string, sellableItemId: string, quantity = 1): DraftLine {
  return { lineId, sellableItemId, quantity, selections: [], comment: '' }
}

const basilLine: DraftLine = {
  ...line('l-basil', 'fixture-item-basil'),
  selections: [
    {
      optionGroupId: FIXTURE_PROTEIN_GROUP.optionGroupId,
      optionId: 'fixture-opt-chicken',
      quantity: 1,
    },
  ],
}

describe('Draft editing', () => {
  it('is bound to device, employee, table session and Master Data version (D10.5, D09)', () => {
    expect(emptyDraft(key, 'md-1')).toEqual({ ...key, sessionMasterDataVersion: 'md-1', lines: [] })
  })

  it('adds, replaces and removes lines without touching the others', () => {
    const two = addLine(addLine(emptyDraft(key, 'md-1'), line('a', 'x')), line('b', 'y'))
    const replaced = replaceLine(two, { ...line('a', 'x', 3), comment: 'no ice' })
    expect(replaced.lines.map((each) => [each.lineId, each.quantity, each.comment])).toEqual([
      ['a', 3, 'no ice'],
      ['b', 1, ''],
    ])
    expect(removeLine(replaced, 'a').lines.map((each) => each.lineId)).toEqual(['b'])
  })

  it('keeps quantity at 1 or more', () => {
    const draft = addLine(emptyDraft(key, 'md-1'), line('a', 'x', 2))
    expect(setLineQuantity(draft, 'a', 0).lines[0].quantity).toBe(1)
    expect(setLineQuantity(draft, 'a', 5).lines[0].quantity).toBe(5)
  })

  it('counts items by quantity', () => {
    const draft = addLine(addLine(emptyDraft(key, 'md-1'), line('a', 'x', 2)), line('b', 'y', 3))
    expect(countItems(draft)).toBe(5)
  })
})

describe('lineProblem', () => {
  it('accepts a line that matches the current menu', () => {
    expect(lineProblem(FIXTURE_MENU, basilLine)).toBeNull()
    expect(lineProblem(FIXTURE_MENU, line('w', 'fixture-item-water'))).toBeNull()
  })

  it('flags an item that is Sold Out or no longer in the menu', () => {
    expect(lineProblem(FIXTURE_MENU, line('g', 'fixture-item-garlic'))).toBe('itemUnavailable')
    expect(lineProblem(FIXTURE_MENU, line('m', 'missing'))).toBe('itemUnavailable')
  })

  it('flags options that broke the rules after the line was added', () => {
    // Squid is Sold Out in the fixture menu.
    const withSquid: DraftLine = {
      ...basilLine,
      selections: [{ ...basilLine.selections[0], optionId: 'fixture-opt-squid' }],
    }
    expect(lineProblem(FIXTURE_MENU, withSquid)).toBe('optionsInvalid')
    expect(lineProblem(FIXTURE_MENU, { ...basilLine, selections: [] })).toBe('optionsInvalid')
  })
})
