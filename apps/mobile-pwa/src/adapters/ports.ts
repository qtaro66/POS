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

export type PairingRejection =
  | 'expired'
  | 'alreadyUsed'
  | 'invalid'
  /** The Branch Service refused the request itself (HTTP 400), for example the device name. */
  | 'invalidRequest'
  /** Too many redemption attempts (HTTP 429). */
  | 'rateLimited'

export type PairingOutcome =
  | { readonly kind: 'paired'; readonly device: PairedDevice }
  | { readonly kind: 'rejected'; readonly reason: PairingRejection }
  | { readonly kind: 'unreachable' }

/**
 * Redeems a pairing QR code. On success the gateway stores the device record
 * and its credential in the `DeviceStore`; the UI only receives the public
 * `PairedDevice` and never handles the credential.
 */
export interface PairingGateway {
  pair(payload: PairingQrPayload, deviceDisplayName: string): Promise<PairingOutcome>
}

export type LoginOutcome =
  | { readonly kind: 'authenticated'; readonly employee: EmployeeSession }
  /** The Branch Service declined the PIN; the reason is deliberately not exposed. */
  | { readonly kind: 'rejected' }
  | { readonly kind: 'unreachable' }

export interface EmployeeAuthGateway {
  login(device: PairedDevice, pin: string): Promise<LoginOutcome>
  logout(device: PairedDevice, employee: EmployeeSession): Promise<void>
  /**
   * Forgets an expired session's token locally without contacting the Branch
   * Service, which already refuses it. There is no automatic token refresh.
   */
  discardExpiredSession(employee: EmployeeSession): void
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

/**
 * Persists the paired device and its revocable device credential on this
 * device (requirement 3.3 / D11). Clearing browser data loses both, and the
 * device must then be paired again.
 */
export interface DeviceStore {
  load(): Promise<PairedDevice | null>
  /** Credential for the stored device, or null when none is stored for that device ID. */
  loadCredential(deviceId: string): Promise<string | null>
  save(device: PairedDevice, credential: string): Promise<void>
}

export interface MobileServices {
  readonly qrScanner: QrScanner
  readonly pairing: PairingGateway
  readonly auth: EmployeeAuthGateway
  readonly tables: ActiveTablesGateway
  readonly syncStatus: SyncStatusSource
  readonly deviceStore: DeviceStore
}
