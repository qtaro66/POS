import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import type { EmployeeAuthGateway } from '../adapters/ports.ts'
import { useFocusOnMount } from '../components/useFocusOnMount.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'

interface LoginScreenProps {
  readonly device: PairedDevice
  readonly auth: EmployeeAuthGateway
  /** Unsent Draft/Pending items kept on this device (requirement D10.5). */
  readonly unsentCount: number
  readonly onAuthenticated: (employee: EmployeeSession) => void
}

/**
 * Employee PIN login on a paired device. The PIN is checked by the Branch
 * Service; this screen never stores it and clears it after every attempt. PIN
 * length and lockout policy are not defined by the requirements, so none is
 * enforced here.
 */
export function LoginScreen({ device, auth, unsentCount, onAuthenticated }: LoginScreenProps) {
  const headingRef = useFocusOnMount<HTMLHeadingElement>()
  const pinRef = useRef<HTMLInputElement>(null)
  const pinId = useId()
  const errorId = useId()
  const [pin, setPin] = useState('')
  const [checking, setChecking] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [focusRequest, setFocusRequest] = useState(0)

  useEffect(() => {
    if (focusRequest > 0) pinRef.current?.focus()
  }, [focusRequest])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (checking) return
    if (pin.length === 0) {
      setError(text.loginPinRequired)
      setFocusRequest((count) => count + 1)
      return
    }

    setChecking(true)
    setError(null)
    let failure: string | null = null
    try {
      const outcome = await auth.login(device, pin)
      if (outcome.kind === 'authenticated') {
        onAuthenticated(outcome.employee)
        return
      }
      failure = outcome.kind === 'rejected' ? text.loginRejected : text.loginUnreachable
    } catch {
      failure = text.loginFailed
    } finally {
      // The PIN is never kept after an attempt, whatever its outcome.
      setPin('')
      setChecking(false)
    }
    setError(failure)
    // Focus moves back once the re-enabled field has been rendered (see effect above).
    setFocusRequest((count) => count + 1)
  }

  return (
    <main className="screen screen--centered" aria-busy={checking}>
      <h1 ref={headingRef} tabIndex={-1}>
        {text.loginTitle}
      </h1>
      <p className="muted">{text.loginDeviceLine(device.deviceName, device.branchName)}</p>
      {unsentCount > 0 && <p className="notice">{text.unsentOnDevice(unsentCount)}</p>}
      <form className="login-form" onSubmit={(event) => void submit(event)} noValidate>
        <label htmlFor={pinId}>{text.loginPinLabel}</label>
        <input
          ref={pinRef}
          id={pinId}
          className="pin-input"
          type="password"
          inputMode="numeric"
          autoComplete="off"
          enterKeyHint="go"
          value={pin}
          onChange={(event) => setPin(event.target.value.replace(/\D/g, ''))}
          aria-invalid={error !== null}
          aria-describedby={error ? errorId : undefined}
          disabled={checking}
        />
        {error && (
          <p id={errorId} role="alert" className="alert">
            {error}
          </p>
        )}
        <button type="submit" className="button button--primary" disabled={checking}>
          {checking ? text.loginInProgress : text.loginSubmit}
        </button>
      </form>
    </main>
  )
}
