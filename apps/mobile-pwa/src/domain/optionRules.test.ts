import { describe, expect, it } from 'vitest'
import {
  FIXTURE_MENU,
  FIXTURE_PROTEIN_GROUP,
  FIXTURE_SAUCE_GROUP,
  FIXTURE_TOPPING_GROUP,
} from '../adapters/fixtureMenu.ts'
import type { MenuItem, OptionGroup } from './menu.ts'
import {
  canIncrease,
  chooseSingle,
  defaultSelections,
  groupProblems,
  itemProblems,
  perOptionLimit,
  selectionCount,
  setOptionQuantity,
  type OptionSelection,
} from './optionRules.ts'

const topping = FIXTURE_TOPPING_GROUP
const [egg, cheese, chili] = topping.options
const protein = FIXTURE_PROTEIN_GROUP
const [chicken, pork, , squid] = protein.options
const sauce = FIXTURE_SAUCE_GROUP

function pick(group: OptionGroup, optionId: string, quantity: number): OptionSelection {
  return { optionGroupId: group.optionGroupId, optionId, quantity }
}

const basil = FIXTURE_MENU.items.find((item) => item.sellableItemId === 'fixture-item-basil') as MenuItem

describe('selectionCount', () => {
  it('counts by quantity, not by kind (D08.4)', () => {
    const selections = [pick(topping, egg.optionId, 2), pick(topping, cheese.optionId, 1)]
    expect(selectionCount(topping, selections)).toBe(3)
  })

  it('ignores selections of other groups', () => {
    expect(selectionCount(topping, [pick(protein, chicken.optionId, 1)])).toBe(0)
  })
})

describe('perOptionLimit', () => {
  it('is 1 without repeats and maxQuantityPerOption with repeats', () => {
    expect(perOptionLimit(sauce)).toBe(1)
    expect(perOptionLimit(topping)).toBe(2)
  })
})

describe('groupProblems', () => {
  it('accepts an optional group with nothing chosen', () => {
    expect(groupProblems(topping, [])).toEqual([])
  })

  it('requires minSelections for a required group', () => {
    expect(groupProblems(protein, [])).toEqual(['belowMinimum'])
    expect(groupProblems(protein, [pick(protein, chicken.optionId, 1)])).toEqual([])
  })

  it('rejects more than maxSelections', () => {
    const selections = [
      pick(topping, egg.optionId, 2),
      pick(topping, cheese.optionId, 1),
      pick(topping, chili.optionId, 1),
    ]
    expect(groupProblems(topping, selections)).toEqual(['aboveMaximum'])
  })

  it('rejects an option repeated beyond its limit', () => {
    expect(groupProblems(topping, [pick(topping, egg.optionId, 3)])).toContain('optionLimit')
    expect(groupProblems(sauce, [pick(sauce, sauce.options[0].optionId, 2)])).toContain('optionLimit')
  })

  it('rejects a Sold Out or unknown option', () => {
    expect(groupProblems(protein, [pick(protein, squid.optionId, 1)])).toEqual(['unavailable'])
    expect(groupProblems(protein, [pick(protein, 'missing', 1)])).toEqual(['unavailable'])
  })
})

describe('canIncrease', () => {
  it('stops at the per-option limit', () => {
    expect(canIncrease(topping, egg, [pick(topping, egg.optionId, 1)])).toBe(true)
    expect(canIncrease(topping, egg, [pick(topping, egg.optionId, 2)])).toBe(false)
  })

  it('stops when the group reaches maxSelections', () => {
    const full = [pick(topping, egg.optionId, 2), pick(topping, cheese.optionId, 1)]
    expect(canIncrease(topping, chili, full)).toBe(false)
  })

  it('never allows a Sold Out option', () => {
    expect(canIncrease(protein, squid, [])).toBe(false)
  })
})

describe('selection helpers', () => {
  it('preselects available defaults only', () => {
    expect(defaultSelections(basil)).toEqual([pick(protein, chicken.optionId, 1)])
    const soldOutDefault: MenuItem = {
      ...basil,
      optionGroups: [
        {
          ...protein,
          options: protein.options.map((option) =>
            option.optionId === chicken.optionId ? { ...option, availability: 'soldOut' } : option,
          ),
        },
      ],
    }
    expect(defaultSelections(soldOutDefault)).toEqual([])
  })

  it('replaces the choice of a single-choice group', () => {
    const first = chooseSingle([], protein, chicken)
    expect(chooseSingle(first, protein, pork)).toEqual([pick(protein, pork.optionId, 1)])
  })

  it('drops an option whose quantity reaches 0', () => {
    const one = setOptionQuantity([], topping, egg, 1)
    expect(one).toEqual([pick(topping, egg.optionId, 1)])
    expect(setOptionQuantity(one, topping, egg, 0)).toEqual([])
  })

  it('reports problems per group, and selections for groups the item does not have', () => {
    expect(itemProblems(basil, []).get(protein.optionGroupId)).toEqual(['belowMinimum'])
    expect(itemProblems(basil, defaultSelections(basil)).size).toBe(0)
    expect(itemProblems(basil, [...defaultSelections(basil), pick(sauce, 'x', 1)]).get('')).toEqual([
      'unavailable',
    ])
  })
})
