import type { MenuItem, MenuOption, OptionGroup } from './menu.ts'

/*
 * Selection rules of D08.4, checked on screen before a line is added. The
 * Branch Service checks them again when the order is submitted and has the
 * final say. Selections are counted by quantity, not by kind: fried egg x2
 * and cheese x1 count as 3.
 */

/** Quantity chosen for one option of one group. */
export interface OptionSelection {
  readonly optionGroupId: string
  readonly optionId: string
  readonly quantity: number
}

export type GroupProblem =
  /** Fewer selections than `minSelections`. */
  | 'belowMinimum'
  /** More selections than `maxSelections`. */
  | 'aboveMaximum'
  /** One option chosen more often than repeats allow. */
  | 'optionLimit'
  /** An option that is Sold Out or no longer in the menu. */
  | 'unavailable'

/** Number of selections in the group, counted by quantity. */
export function selectionCount(group: OptionGroup, selections: readonly OptionSelection[]): number {
  return selections
    .filter((selection) => selection.optionGroupId === group.optionGroupId)
    .reduce((count, selection) => count + selection.quantity, 0)
}

/** Most units of one option the group allows: 1 without repeats, else `maxQuantityPerOption`. */
export function perOptionLimit(group: OptionGroup): number {
  return group.allowRepeatedOption ? group.maxQuantityPerOption : 1
}

export function quantityOf(selections: readonly OptionSelection[], group: OptionGroup, option: MenuOption): number {
  return (
    selections.find(
      (selection) =>
        selection.optionGroupId === group.optionGroupId && selection.optionId === option.optionId,
    )?.quantity ?? 0
  )
}

export function groupProblems(
  group: OptionGroup,
  selections: readonly OptionSelection[],
): readonly GroupProblem[] {
  const own = selections.filter(
    (selection) => selection.optionGroupId === group.optionGroupId && selection.quantity > 0,
  )
  const problems: GroupProblem[] = []
  const count = own.reduce((total, selection) => total + selection.quantity, 0)
  if (count < group.minSelections) problems.push('belowMinimum')
  if (count > group.maxSelections) problems.push('aboveMaximum')
  if (own.some((selection) => selection.quantity > perOptionLimit(group))) {
    problems.push('optionLimit')
  }
  const unavailable = own.some((selection) => {
    const option = group.options.find((candidate) => candidate.optionId === selection.optionId)
    return !option || option.availability !== 'available'
  })
  if (unavailable) problems.push('unavailable')
  return problems
}

/** Whether one more unit of the option may be chosen without breaking a rule. */
export function canIncrease(
  group: OptionGroup,
  option: MenuOption,
  selections: readonly OptionSelection[],
): boolean {
  return (
    option.availability === 'available' &&
    quantityOf(selections, group, option) < perOptionLimit(group) &&
    selectionCount(group, selections) < group.maxSelections
  )
}

/** Problems of every group of the item, keyed by group; an empty map means the selection is valid. */
export function itemProblems(
  item: MenuItem,
  selections: readonly OptionSelection[],
): ReadonlyMap<string, readonly GroupProblem[]> {
  const problems = new Map<string, readonly GroupProblem[]>()
  for (const group of item.optionGroups) {
    const found = groupProblems(group, selections)
    if (found.length > 0) problems.set(group.optionGroupId, found)
  }
  const known = new Set(item.optionGroups.map((group) => group.optionGroupId))
  if (selections.some((selection) => selection.quantity > 0 && !known.has(selection.optionGroupId))) {
    problems.set('', ['unavailable'])
  }
  return problems
}

/**
 * Starting selections for a new line: each available default option once.
 * A default that is Sold Out is not preselected (D08.4).
 */
export function defaultSelections(item: MenuItem): readonly OptionSelection[] {
  return item.optionGroups.flatMap((group) =>
    group.options
      .filter((option) => option.isDefault && option.availability === 'available')
      .slice(0, group.maxSelections)
      .map((option) => ({ optionGroupId: group.optionGroupId, optionId: option.optionId, quantity: 1 })),
  )
}

/** Sets the quantity of one option, dropping it when the quantity reaches 0. */
export function setOptionQuantity(
  selections: readonly OptionSelection[],
  group: OptionGroup,
  option: MenuOption,
  quantity: number,
): readonly OptionSelection[] {
  const others = selections.filter(
    (selection) =>
      !(selection.optionGroupId === group.optionGroupId && selection.optionId === option.optionId),
  )
  return quantity > 0
    ? [...others, { optionGroupId: group.optionGroupId, optionId: option.optionId, quantity }]
    : others
}

/**
 * Chooses an option in a group that allows exactly one selection, replacing
 * the previous choice, as a radio group does.
 */
export function chooseSingle(
  selections: readonly OptionSelection[],
  group: OptionGroup,
  option: MenuOption,
): readonly OptionSelection[] {
  return [
    ...selections.filter((selection) => selection.optionGroupId !== group.optionGroupId),
    { optionGroupId: group.optionGroupId, optionId: option.optionId, quantity: 1 },
  ]
}

/** A group with `maxSelections` 1 is shown as a single choice. */
export function isSingleChoice(group: OptionGroup): boolean {
  return group.maxSelections === 1
}
