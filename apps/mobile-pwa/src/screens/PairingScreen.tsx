import { useEffect, useId, useRef, useState, type FormEvent } from 'react'
import type { PairingGateway, PairingRejection, QrScanner } from '../adapters/ports.ts'
import { useFocusOnMount } from '../components/useFocusOnMount.ts'
import { parsePairingQrPayload, type PairingQrRejection } from '../domain/pairingQr.ts'
import type { PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'

interface PairingScreenProps {
  readonly scanner: QrScanner
  readonly pairing: PairingGateway
  readonly onPaired: (device: PairedDevice) => void
}

const pairingRejectionMessage: Record<PairingRejection, string> = {
  expired: text.pairingExpired,
  alreadyUsed: text.pairingAlreadyUsed,
  invalid: text.pairingInvalid,
  invalidRequest: text.pairingInvalidRequest,
  rateLimited: text.pairingRateLimited,
}

const qrRejectionMessage: Record<PairingQrRejection, string> = {
  insecureOrigin: text.pairingInsecure,
  unsupportedVersion: text.pairingUnsupportedVersion,
  malformed: text.pairingMalformed,
}

type PairingUiState =
  | { readonly phase: 'idle' }
  | { readonly phase: 'working' }
  | { readonly phase: 'error'; readonly message: string }

export function PairingScreen({ scanner, pairing, onPaired }: PairingScreenProps) {
  const headingRef = useFocusOnMount<HTMLHeadingElement>()
  const nameRef = useRef<HTMLInputElement>(null)
  const nameId = useId()
  const errorId = useId()
  const [deviceName, setDeviceName] = useState('')
  const [ui, setUi] = useState<PairingUiState>({ phase: 'idle' })
  const [focusName, setFocusName] = useState(0)
  const working = ui.phase === 'working'

  useEffect(() => {
    if (focusName > 0) nameRef.current?.focus()
  }, [focusName])

  async function scanAndPair(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (working) return
    if (deviceName.trim().length === 0) {
      setUi({ phase: 'error', message: text.deviceNameRequired })
      setFocusName((count) => count + 1)
      return
    }
    setUi({ phase: 'working' })
    let message: string
    try {
      message = await attemptPairing()
    } catch {
      message = text.pairingFailed
    }
    if (message) setUi({ phase: 'error', message })
  }

  /** Returns an error message, or an empty string once the device is paired. */
  async function attemptPairing(): Promise<string> {
    const scan = await scanner.scan()
    if (scan.kind === 'cancelled') return text.pairingScanCancelled
    if (scan.kind === 'unavailable') return text.pairingScannerUnavailable

    const parsed = parsePairingQrPayload(scan.text)
    if (!parsed.ok) return qrRejectionMessage[parsed.reason]

    const outcome = await pairing.pair(parsed.payload, deviceName.trim())
    switch (outcome.kind) {
      case 'paired':
        onPaired(outcome.device)
        return ''
      case 'unreachable':
        return text.pairingUnreachable
      case 'rejected':
        return pairingRejectionMessage[outcome.reason]
    }
  }

  return (
    <main className="screen screen--centered" aria-busy={working}>
      <h1 ref={headingRef} tabIndex={-1}>
        {text.pairingTitle}
      </h1>
      <p>{text.pairingIntro}</p>
      <p className="muted">{text.pairingRules}</p>
      <form className="login-form" onSubmit={(event) => void scanAndPair(event)} noValidate>
        <label htmlFor={nameId}>{text.deviceNameLabel}</label>
        <input
          ref={nameRef}
          id={nameId}
          className="text-input"
          type="text"
          autoComplete="off"
          value={deviceName}
          onChange={(event) => setDeviceName(event.target.value)}
          aria-required="true"
          aria-invalid={ui.phase === 'error' && ui.message === text.deviceNameRequired}
          aria-describedby={ui.phase === 'error' ? errorId : undefined}
          disabled={working}
        />
        {ui.phase === 'error' && (
          <p id={errorId} role="alert" className="alert">
            {ui.message}
          </p>
        )}
        <button type="submit" className="button button--primary" disabled={working}>
          {working ? text.pairingInProgress : text.pairingScan}
        </button>
      </form>
    </main>
  )
}
