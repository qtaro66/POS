import { useId, useRef, useState, type FormEvent } from 'react'
import { formatPrice } from '../components/formatPrice.ts'
import { Alert } from '../components/Messages.tsx'
import { Screen } from '../components/Screen.tsx'
import { Stepper } from '../components/Stepper.tsx'
import type { DraftLine } from '../domain/draft.ts'
import { orderedOptionGroups, type MenuItem, type OptionGroup } from '../domain/menu.ts'
import {
  canIncrease,
  chooseSingle,
  defaultSelections,
  groupProblems,
  itemProblems,
  perOptionLimit,
  quantityOf,
  selectionCount,
  setOptionQuantity,
  type GroupProblem,
  type OptionSelection,
} from '../domain/optionRules.ts'
import { text } from '../strings.ts'

interface ItemScreenProps {
  readonly item: MenuItem
  /** The basket line being edited, or null to add a new line. */
  readonly line: DraftLine | null
  readonly onSave: (line: Omit<DraftLine, 'lineId'>) => void
}

function problemText(group: OptionGroup, problem: GroupProblem): string {
  switch (problem) {
    case 'belowMinimum':
      return text.groupBelowMinimum(group.minSelections)
    case 'aboveMaximum':
      return text.groupAboveMaximum(group.maxSelections)
    case 'optionLimit':
      return text.groupOptionLimit
    case 'unavailable':
      return text.groupUnavailable
  }
}

/**
 * Options, quantity and note of one item. SubItem groups come before Topping
 * groups (D18.6). The D08.4 rules are shown for every group and enforced
 * before the line is added; the Branch Service checks them again on submit.
 */
export function ItemScreen({ item, line, onSave }: ItemScreenProps) {
  const [selections, setSelections] = useState<readonly OptionSelection[]>(
    () => line?.selections ?? defaultSelections(item),
  )
  const [quantity, setQuantity] = useState(line?.quantity ?? 1)
  const [comment, setComment] = useState(line?.comment ?? '')
  const [showProblems, setShowProblems] = useState(false)
  const groupRefs = useRef(new Map<string, HTMLFieldSetElement>())
  const commentId = useId()
  const groups = orderedOptionGroups(item)

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const problems = itemProblems(item, selections)
    if (problems.size > 0) {
      setShowProblems(true)
      const first = groups.find((group) => problems.has(group.optionGroupId))
      if (first) groupRefs.current.get(first.optionGroupId)?.focus()
      return
    }
    onSave({
      sellableItemId: item.sellableItemId,
      quantity,
      selections: selections.filter((selection) => selection.quantity > 0),
      comment: comment.trim(),
    })
  }

  return (
    <Screen
      title={item.name}
      subtitle={
        <p className="muted">
          {item.code} · {formatPrice(item.price)}
        </p>
      }
    >
      <form className="item-form" onSubmit={submit} noValidate>
        {showProblems && itemProblems(item, selections).size > 0 && (
          <Alert>
            <p>{text.itemFixProblems}</p>
          </Alert>
        )}

        {groups.map((group) => (
          <OptionGroupField
            key={group.optionGroupId}
            group={group}
            selections={selections}
            showProblems={showProblems}
            onChange={setSelections}
            fieldRef={(element) => {
              if (element) groupRefs.current.set(group.optionGroupId, element)
              else groupRefs.current.delete(group.optionGroupId)
            }}
          />
        ))}

        <div className="field">
          <span className="field__label">{text.itemQuantity}</span>
          <Stepper
            label={text.itemQuantity}
            value={quantity}
            decreaseLabel={text.itemDecrease}
            increaseLabel={text.itemIncrease}
            canDecrease={quantity > 1}
            canIncrease
            onChange={(next) => setQuantity(Math.max(1, next))}
          />
        </div>

        <div className="field">
          <label className="field__label" htmlFor={commentId}>
            {text.itemComment}
          </label>
          <textarea
            id={commentId}
            className="text-input"
            rows={2}
            value={comment}
            onChange={(event) => setComment(event.target.value)}
          />
        </div>

        <button type="submit" className="button button--primary">
          {line ? text.itemUpdate : text.itemAdd}
        </button>
      </form>
    </Screen>
  )
}

interface OptionGroupFieldProps {
  readonly group: OptionGroup
  readonly selections: readonly OptionSelection[]
  readonly showProblems: boolean
  readonly onChange: (selections: readonly OptionSelection[]) => void
  readonly fieldRef: (element: HTMLFieldSetElement | null) => void
}

function OptionGroupField({ group, selections, showProblems, onChange, fieldRef }: OptionGroupFieldProps) {
  const ruleId = useId()
  const problemId = useId()
  const problems = showProblems ? groupProblems(group, selections) : []
  const single = group.minSelections === 1 && group.maxSelections === 1
  const oncePerOption = perOptionLimit(group) === 1
  const count = selectionCount(group, selections)

  return (
    <fieldset
      ref={fieldRef}
      className="option-group"
      tabIndex={-1}
      aria-describedby={problems.length > 0 ? `${ruleId} ${problemId}` : ruleId}
      aria-invalid={problems.length > 0 || undefined}
    >
      <legend className="option-group__name">{group.name}</legend>
      <p id={ruleId} className="muted">
        {text.groupRule(group.minSelections, group.maxSelections)}
        {!single && ` · ${text.groupChosen(count, group.maxSelections)}`}
      </p>
      {problems.length > 0 && (
        <p id={problemId} className="option-group__problem">
          {problems.map((problem) => problemText(group, problem)).join(' ')}
        </p>
      )}
      <ul className="option-list">
        {group.options.map((option) => {
          const quantity = quantityOf(selections, group, option)
          const soldOut = option.availability !== 'available'
          const label = (
            <>
              <span>{option.name}</span>
              <span className="muted">
                {soldOut ? text.menuSoldOut : option.price > 0 ? `+${formatPrice(option.price)}` : ''}
              </span>
            </>
          )
          if (single || oncePerOption) {
            return (
              <li key={option.optionId}>
                <label className="option">
                  <input
                    type={single ? 'radio' : 'checkbox'}
                    name={group.optionGroupId}
                    checked={quantity > 0}
                    // A chosen option that became Sold Out stays enabled so it can be cleared.
                    disabled={
                      quantity === 0 &&
                      (soldOut || (!single && !canIncrease(group, option, selections)))
                    }
                    onChange={(event) =>
                      onChange(
                        single
                          ? chooseSingle(selections, group, option)
                          : setOptionQuantity(selections, group, option, event.target.checked ? 1 : 0),
                      )
                    }
                  />
                  {label}
                </label>
              </li>
            )
          }
          return (
            <li key={option.optionId} className="option option--stepper">
              <span className="option__label">{label}</span>
              <Stepper
                label={text.optionQuantity(option.name)}
                value={quantity}
                decreaseLabel={text.optionDecrease(option.name)}
                increaseLabel={text.optionIncrease(option.name)}
                canDecrease={quantity > 0}
                canIncrease={canIncrease(group, option, selections)}
                onChange={(next) => onChange(setOptionQuantity(selections, group, option, next))}
              />
            </li>
          )
        })}
      </ul>
    </fieldset>
  )
}
