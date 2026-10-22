import type {
  ActiveTablesSnapshot,
  EmployeeSession,
  PairedDevice,
  PairingQrPayload,
  SyncStatus,
} from '../domain/types.ts'

/**
 * Replaceable boundaries between the Mobile UI and the outside world. The
 * Phase 0 foundation ships only deterministic fixture implementations; real
 * implementations will call the Branch API through the OpenAPI-generated
 * client (requirement D17) and must never reach SQLite or printers directly.
 * Callers treat a rejected promise as an unexpected failure and recover.
 */

export type QrScanResult =
  /** Raw QR text; it is validated by `parsePairingQrPayload` before use. */
  | { readonly kind: 'scanned'; readonly text: string }
  | { readonly kind: 'cancelled' }
  | { readonly kind: 'unavailable' }

/** Reads the text of a pairing QR code (for example from the device camera). */
export interface QrScanner {
  scan(): Promise<QrScanResult>
}

export type PairingOutcome =
  | { readonly kind: 'paired'; readonly device: PairedDevice }
  | { readonly kind: 'rejected'; readonly reason: 'expired' | 'alreadyUsed' | 'invalid' }
  | { readonly kind: 'unreachable' }

export interface PairingGateway {
  pair(payload: PairingQrPayload): Promise<PairingOutcome>
}

export type LoginOutcome =
  | { readonly kind: 'authenticated'; readonly employee: EmployeeSession }
  /** The Branch Service declined the PIN; the reason is deliberately not exposed. */
  | { readonly kind: 'rejected' }
  | { readonly kind: 'unreachable' }

export interface EmployeeAuthGateway {
  login(device: PairedDevice, pin: string): Promise<LoginOutcome>
  logout(device: PairedDevice, employee: EmployeeSession): Promise<void>
}

export type ActiveTablesOutcome =
  | { readonly kind: 'ok'; readonly snapshot: ActiveTablesSnapshot }
  | { readonly kind: 'unreachable' }

export interface ActiveTablesGateway {
  listActiveTables(device: PairedDevice): Promise<ActiveTablesOutcome>
}

/** Branch connectivity, employee-data sync time and local outbox counts. */
export interface SyncStatusSource {
  getStatus(): SyncStatus
  subscribe(listener: () => void): () => void
}

/** Persists the paired-device record on this device. */
export interface DeviceStore {
  load(): PairedDevice | null
  save(device: PairedDevice | null): void
}

export interface MobileServices {
  readonly qrScanner: QrScanner
  readonly pairing: PairingGateway
  readonly auth: EmployeeAuthGateway
  readonly tables: ActiveTablesGateway
  readonly syncStatus: SyncStatusSource
  readonly deviceStore: DeviceStore
}
