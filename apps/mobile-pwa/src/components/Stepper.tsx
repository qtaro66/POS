interface StepperProps {
  /** Names the whole control, for example "Quantity of Water". */
  readonly label: string
  readonly value: number
  readonly decreaseLabel: string
  readonly increaseLabel: string
  readonly canDecrease: boolean
  readonly canIncrease: boolean
  readonly onChange: (value: number) => void
}

/**
 * Minus / value / plus control with 44 px buttons. The buttons carry their own
 * names, and the value is announced politely when it changes.
 */
export function Stepper({
  label,
  value,
  decreaseLabel,
  increaseLabel,
  canDecrease,
  canIncrease,
  onChange,
}: StepperProps) {
  return (
    <div className="stepper" role="group" aria-label={label}>
      <button
        type="button"
        className="stepper__button"
        aria-label={decreaseLabel}
        disabled={!canDecrease}
        onClick={() => onChange(value - 1)}
      >
        −
      </button>
      <span className="stepper__value" aria-live="polite">
        {value}
      </span>
      <button
        type="button"
        className="stepper__button"
        aria-label={increaseLabel}
        disabled={!canIncrease}
        onClick={() => onChange(value + 1)}
      >
        +
      </button>
    </div>
  )
}
