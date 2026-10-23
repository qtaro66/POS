import { useId, useRef } from 'react'
import { countLineItems, lineProblem, type DraftLine } from '../domain/draft.ts'
import { findItem, type Menu } from '../domain/menu.ts'
import { text } from '../strings.ts'
import { formatPrice } from './formatPrice.ts'
import { Alert, Notice } from './Messages.tsx'
import { Stepper } from './Stepper.tsx'

interface BasketProps {
  readonly lines: readonly DraftLine[]
  /** Menu used to name lines and flag those that can no longer be sent; null while it loads. */
  readonly menu: Menu | null
  readonly saveFailed: boolean
  readonly onQuantity: (line: DraftLine, quantity: number) => void
  readonly onEdit: (line: DraftLine) => void
  readonly onRemove: (line: DraftLine) => void
}

function optionSummary(menu: Menu, line: DraftLine): string {
  const item = findItem(menu, line.sellableItemId)
  return line.selections
    .map((selection) => {
      const group = item?.optionGroups.find((candidate) => candidate.optionGroupId === selection.optionGroupId)
      const option = group?.options.find((candidate) => candidate.optionId === selection.optionId)
      const name = option?.name ?? selection.optionId
      const price = option && option.price > 0 ? ` +${formatPrice(option.price)}` : ''
      return selection.quantity > 1 ? `${name} ×${selection.quantity}${price}` : `${name}${price}`
    })
    .join(', ')
}

/**
 * The table's basket on this device (the Draft, D18.6). Lines can change
 * quantity, be edited or removed until they are sent. Prices are shown as
 * resolved by the Branch; Mobile shows no totals (D10.1).
 */
export function Basket({ lines, menu, saveFailed, onQuantity, onEdit, onRemove }: BasketProps) {
  const headingId = useId()
  const headingRef = useRef<HTMLHeadingElement>(null)
  const count = countLineItems(lines)

  return (
    <section className="panel" aria-labelledby={headingId}>
      <h2 id={headingId} ref={headingRef} tabIndex={-1}>
        {text.basketHeading}
      </h2>
      <p className="muted">{count === 0 ? text.basketEmpty : text.basketCount(count)}</p>
      {lines.length > 0 && (
        <ul className="basket-list" aria-label={text.basketHeading}>
          {lines.map((line) => {
            const item = menu ? findItem(menu, line.sellableItemId) : undefined
            const name = item?.name ?? line.sellableItemId
            const problem = menu ? lineProblem(menu, line) : null
            const summary = menu ? optionSummary(menu, line) : ''
            return (
              <li key={line.lineId} className="basket-line">
                <div className="basket-line__text">
                  <span className="basket-line__name">{name}</span>
                  {item && (
                    <span className="muted">
                      {text.basketUnitPrice} {formatPrice(item.price)}
                    </span>
                  )}
                  {summary && <span>{summary}</span>}
                  {line.comment && (
                    <span>
                      {text.basketComment}: {line.comment}
                    </span>
                  )}
                  {problem && (
                    <span className="basket-line__problem">
                      {problem === 'itemUnavailable' ? text.basketItemUnavailable : text.basketOptionsInvalid}
                    </span>
                  )}
                </div>
                <div className="basket-line__actions">
                  <Stepper
                    label={text.basketQuantity(name)}
                    value={line.quantity}
                    decreaseLabel={text.basketDecrease(name)}
                    increaseLabel={text.basketIncrease(name)}
                    canDecrease={line.quantity > 1}
                    canIncrease
                    onChange={(quantity) => onQuantity(line, quantity)}
                  />
                  {item && item.optionGroups.length > 0 && (
                    <button
                      type="button"
                      className="button button--compact"
                      aria-label={text.basketEditLabel(name)}
                      onClick={() => onEdit(line)}
                    >
                      {text.basketEdit}
                    </button>
                  )}
                  <button
                    type="button"
                    className="button button--compact"
                    aria-label={text.basketRemoveLabel(name)}
                    onClick={() => {
                      onRemove(line)
                      // The removed line's buttons disappear; keep focus inside the basket.
                      headingRef.current?.focus()
                    }}
                  >
                    {text.basketRemove}
                  </button>
                </div>
              </li>
            )
          })}
        </ul>
      )}
      {saveFailed && (
        <Alert>
          <p>{text.basketSaveFailed}</p>
        </Alert>
      )}
      <Notice>
        <p>{text.basketMemoryOnly}</p>
      </Notice>
    </section>
  )
}
