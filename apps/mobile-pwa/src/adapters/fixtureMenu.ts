import type { Draft, DraftKey } from '../domain/draft.ts'
import type { Menu, MenuOption, OptionGroup } from '../domain/menu.ts'
import type { DraftRepository } from './ports.ts'

/*
 * Synthetic menu for development and tests. Names, codes and prices are
 * invented; they do not come from any real branch package.
 */

export const FIXTURE_MASTER_DATA_VERSION = 'fixture-md-0001'

function option(
  optionId: string,
  name: string,
  price: number,
  extra: Partial<Pick<MenuOption, 'availability' | 'isDefault'>> = {},
): MenuOption {
  return { optionId, name, price, availability: 'available', isDefault: false, ...extra }
}

/** Required single choice: exactly one protein. */
export const FIXTURE_PROTEIN_GROUP: OptionGroup = {
  optionGroupId: 'fixture-group-protein',
  name: 'Protein',
  optionType: 'SubItem',
  minSelections: 1,
  maxSelections: 1,
  allowRepeatedOption: false,
  maxQuantityPerOption: 1,
  options: [
    option('fixture-opt-chicken', 'Chicken', 0, { isDefault: true }),
    option('fixture-opt-pork', 'Pork', 0),
    option('fixture-opt-shrimp', 'Shrimp', 3000),
    option('fixture-opt-squid', 'Squid', 3000, { availability: 'soldOut' }),
  ],
}

/** Optional toppings: up to 3 in total, fried egg up to 2. */
export const FIXTURE_TOPPING_GROUP: OptionGroup = {
  optionGroupId: 'fixture-group-topping',
  name: 'Toppings',
  optionType: 'Topping',
  minSelections: 0,
  maxSelections: 3,
  allowRepeatedOption: true,
  maxQuantityPerOption: 2,
  options: [
    option('fixture-opt-egg', 'Fried egg', 1000),
    option('fixture-opt-cheese', 'Cheese', 1500),
    option('fixture-opt-chili', 'Extra chili', 0),
  ],
}

/** Required choice of 1 to 2 without repeats. */
export const FIXTURE_SAUCE_GROUP: OptionGroup = {
  optionGroupId: 'fixture-group-sauce',
  name: 'Dipping sauce',
  optionType: 'Topping',
  minSelections: 1,
  maxSelections: 2,
  allowRepeatedOption: false,
  maxQuantityPerOption: 1,
  options: [
    option('fixture-opt-sweet-chili', 'Sweet chili', 0),
    option('fixture-opt-seafood', 'Seafood sauce', 0),
    option('fixture-opt-plum', 'Plum sauce', 0),
  ],
}

export const FIXTURE_MENU: Menu = {
  masterDataVersion: FIXTURE_MASTER_DATA_VERSION,
  categories: [
    { categoryId: 'fixture-cat-stir-fry', name: 'Stir-fry' },
    { categoryId: 'fixture-cat-fried', name: 'Fried' },
    { categoryId: 'fixture-cat-drinks', name: 'Drinks' },
  ],
  items: [
    {
      sellableItemId: 'fixture-item-basil',
      code: 'F01',
      name: 'Basil stir-fry',
      categoryId: 'fixture-cat-stir-fry',
      price: 6000,
      availability: 'available',
      optionGroups: [FIXTURE_PROTEIN_GROUP, FIXTURE_TOPPING_GROUP],
    },
    {
      sellableItemId: 'fixture-item-garlic',
      code: 'F02',
      name: 'Garlic stir-fry',
      categoryId: 'fixture-cat-stir-fry',
      price: 6000,
      availability: 'soldOut',
      optionGroups: [FIXTURE_PROTEIN_GROUP],
    },
    {
      sellableItemId: 'fixture-item-spring-rolls',
      code: 'T01',
      name: 'Spring rolls',
      categoryId: 'fixture-cat-fried',
      price: 8000,
      availability: 'available',
      optionGroups: [FIXTURE_SAUCE_GROUP],
    },
    {
      sellableItemId: 'fixture-item-water',
      code: 'D01',
      name: 'Water',
      categoryId: 'fixture-cat-drinks',
      price: 1500,
      availability: 'available',
      optionGroups: [],
    },
    {
      sellableItemId: 'fixture-item-iced-tea',
      code: 'D02',
      name: 'Iced tea',
      categoryId: 'fixture-cat-drinks',
      price: 3500,
      availability: 'available',
      optionGroups: [],
    },
  ],
}

function keyOf(key: DraftKey): string {
  return `${key.deviceId}\u0000${key.employeeId}\u0000${key.tableSessionId}`
}

/**
 * Draft repository that keeps baskets in memory only, for tests: they are
 * lost when the app closes or reloads. The app uses `IndexedDbOrderStore`.
 */
export class MemoryDraftRepository implements DraftRepository {
  readonly persistence = 'memory'
  private readonly drafts = new Map<string, Draft>()

  load(key: DraftKey): Promise<Draft | null> {
    return Promise.resolve(this.drafts.get(keyOf(key)) ?? null)
  }

  save(draft: Draft): Promise<void> {
    this.drafts.set(keyOf(draft), draft)
    return Promise.resolve()
  }
}
