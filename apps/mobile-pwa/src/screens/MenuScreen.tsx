import { useState } from 'react'
import { LiveMessage } from '../components/LiveMessage.tsx'
import { Alert } from '../components/Messages.tsx'
import { formatPrice } from '../components/formatPrice.ts'
import { Screen } from '../components/Screen.tsx'
import type { MenuData } from '../components/useMenu.ts'
import { countItems, type Draft } from '../domain/draft.ts'
import type { MenuItem } from '../domain/menu.ts'
import type { CurrentTable } from '../domain/shellRoute.ts'
import { text } from '../strings.ts'

interface MenuScreenProps {
  readonly table: CurrentTable
  readonly menu: MenuData
  readonly draft: Draft | null
  /** Confirmation of the last basket change, announced politely. */
  readonly message: string | null
  readonly onChooseItem: (item: MenuItem) => void
  readonly onViewBasket: () => void
}

/**
 * Menu of the table session, filtered by category as in the confirmed PC POS
 * layout (D18.6): code, name, price and Sold Out. Sold Out items stay visible
 * and focusable but cannot be added (D08.2). Items without options are added
 * with one tap; items with options open their options screen.
 */
export function MenuScreen({ table, menu, draft, message, onChooseItem, onViewBasket }: MenuScreenProps) {
  const [categoryId, setCategoryId] = useState<string | null>(null)
  const { state } = menu
  const basketCount = draft ? countItems(draft) : 0

  return (
    <Screen title={text.menuTitle(table.tableLabel)} busy={state.status === 'loading'}>
      <LiveMessage message={message} />
      <div className="toolbar">
        <button type="button" className="button" onClick={onViewBasket}>
          {text.menuViewBasket(basketCount)}
        </button>
      </div>

      {state.status === 'loading' && <p className="muted">{text.menuLoading}</p>}
      {(state.status === 'unreachable' || state.status === 'failed') && (
        <Alert>
          <p>{state.status === 'unreachable' ? text.menuUnreachable : text.menuFailed}</p>
          <button type="button" className="button" onClick={menu.retry}>
            {text.menuRetry}
          </button>
        </Alert>
      )}

      {state.status === 'ready' && (
        <>
          <div className="chips" role="group" aria-label={text.menuCategories}>
            {[{ categoryId: null, name: text.menuAllCategories }, ...state.menu.categories].map(
              (category) => (
                <button
                  key={category.categoryId ?? 'all'}
                  type="button"
                  className="chip"
                  aria-pressed={categoryId === category.categoryId}
                  onClick={() => setCategoryId(category.categoryId)}
                >
                  {category.name}
                </button>
              ),
            )}
          </div>
          <ul className="menu-list" aria-label={text.menuTitle(table.tableLabel)}>
            {state.menu.items
              .filter((item) => categoryId === null || item.categoryId === categoryId)
              .map((item) => {
                const soldOut = item.availability !== 'available'
                return (
                  <li key={item.sellableItemId}>
                    <button
                      type="button"
                      className="menu-item"
                      aria-disabled={soldOut || undefined}
                      onClick={() => {
                        if (!soldOut) onChooseItem(item)
                      }}
                    >
                      <span className="menu-item__name">{item.name}</span>
                      <span className="menu-item__meta">
                        <span className="muted">{item.code}</span>
                        <span>{formatPrice(item.price)}</span>
                        {soldOut ? (
                          <span className="badge badge--soldOut">{text.menuSoldOut}</span>
                        ) : (
                          item.optionGroups.length > 0 && (
                            <span className="badge">{text.menuHasOptions}</span>
                          )
                        )}
                      </span>
                    </button>
                  </li>
                )
              })}
          </ul>
        </>
      )}
    </Screen>
  )
}
