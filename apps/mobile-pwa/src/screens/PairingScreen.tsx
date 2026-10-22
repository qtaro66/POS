import { useState } from 'react'
import type { PairingGateway, QrScanner } from '../adapters/ports.ts'
import { useFocusOnMount } from '../components/useFocusOnMount.ts'
import { parsePairingQrPayload, type PairingQrRejection } from '../domain/pairingQr.ts'
import type { PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'

interface PairingScreenProps {
  readonly scanner: QrScanner
  readonly pairing: PairingGateway
  readonly onPaired: (device: PairedDevice) => void
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
  const [ui, setUi] = useState<PairingUiState>({ phase: 'idle' })
  const working = ui.phase === 'working'

  async function scanAndPair() {
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

    const outcome = await pairing.pair(parsed.payload)
    switch (outcome.kind) {
      case 'paired':
        onPaired(outcome.device)
        return ''
      case 'unreachable':
        return text.pairingUnreachable
      case 'rejected':
        return outcome.reason === 'expired'
          ? text.pairingExpired
          : outcome.reason === 'alreadyUsed'
            ? text.pairingAlreadyUsed
            : text.pairingInvalid
    }
  }

  return (
    <main className="screen screen--centered" aria-busy={working}>
      <h1 ref={headingRef} tabIndex={-1}>
        {text.pairingTitle}
      </h1>
      <p>{text.pairingIntro}</p>
      <p className="muted">{text.pairingRules}</p>
      {ui.phase === 'error' && (
        <p role="alert" className="alert">
          {ui.message}
        </p>
      )}
      <button
        type="button"
        className="button button--primary"
        onClick={() => void scanAndPair()}
        disabled={working}
      >
        {working ? text.pairingInProgress : text.pairingScan}
      </button>
    </main>
  )
}
