/**
 * Mobile view model of the menu for one table session (requirements D08.3,
 * D08.4, D09). The Branch Service resolves prices and sellability; Mobile only
 * displays them and never calculates bill totals (D10.1). The Branch contract
 * for this menu does not exist yet (backlog CAP-07), so only fixtures fill it.
 */

/** Price in the smallest currency unit (satang), as resolved by the Branch Service. */
export type MinorAmount = number

/**
 * Whether an item or option can be added right now. Items that are not
 * `CentralActive` or not `BranchCatalogEnabled` are left out of the menu
 * entirely; `soldOut` is the Branch operational override (D08.2, D08.3).
 */
export type Availability = 'available' | 'soldOut'

export type OptionType = 'SubItem' | 'Topping'

export interface MenuOption {
  readonly optionId: string
  readonly name: string
  /** Additional price for one unit of this option; 0 when it adds nothing. */
  readonly price: MinorAmount
  readonly availability: Availability
  readonly isDefault: boolean
}

/** Selection rules of one SubItem or Topping group (D08.4). */
export interface OptionGroup {
  readonly optionGroupId: string
  readonly name: string
  readonly optionType: OptionType
  /** 0 means optional; more than 0 means required. */
  readonly minSelections: number
  readonly maxSelections: number
  readonly allowRepeatedOption: boolean
  readonly maxQuantityPerOption: number
  readonly options: readonly MenuOption[]
}

export interface MenuItem {
  readonly sellableItemId: string
  readonly code: string
  readonly name: string
  readonly categoryId: string
  readonly price: MinorAmount
  readonly availability: Availability
  readonly optionGroups: readonly OptionGroup[]
}

export interface MenuCategory {
  readonly categoryId: string
  readonly name: string
}

export interface Menu {
  /** Master Data version fixed for the table session when it was opened (D09). */
  readonly masterDataVersion: string
  readonly categories: readonly MenuCategory[]
  readonly items: readonly MenuItem[]
}

/** SubItem groups are chosen before Toppings, as in the confirmed PC POS flow (D18.6). */
export function orderedOptionGroups(item: MenuItem): readonly OptionGroup[] {
  return [
    ...item.optionGroups.filter((group) => group.optionType === 'SubItem'),
    ...item.optionGroups.filter((group) => group.optionType === 'Topping'),
  ]
}

export function findItem(menu: Menu, sellableItemId: string): MenuItem | undefined {
  return menu.items.find((item) => item.sellableItemId === sellableItemId)
}
