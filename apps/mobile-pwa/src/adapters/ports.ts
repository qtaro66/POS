import type { Draft, DraftKey } from '../domain/draft.ts'
import type { Menu } from '../domain/menu.ts'
import type { MobileRequest } from '../domain/mobileRequest.ts'
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

export type MenuOutcome =
  | { readonly kind: 'ok'; readonly menu: Menu }
  | { readonly kind: 'unreachable' }

/**
 * Menu of one table session, with the prices and option structure fixed when
 * the session was opened and the current sellability (D08.3, D08.4, D09).
 * No Branch endpoint exists yet (backlog CAP-07); only fixtures implement it.
 */
export interface MenuGateway {
  loadMenu(device: PairedDevice, tableSessionId: string): Promise<MenuOutcome>
}

/**
 * Holds the Draft (basket) of a table session on this device. Drafts must be
 * persistent on the device (D10.5); `IndexedDbOrderStore` is the real
 * implementation and `MemoryDraftRepository` is for tests only.
 */
export interface DraftRepository {
  /** Where saved Drafts live, so the UI never claims more than is true. */
  readonly persistence: 'memory' | 'device'
  load(key: DraftKey): Promise<Draft | null>
  /** Resolves only after the Draft is stored. */
  save(draft: Draft): Promise<void>
}

/**
 * The device's persistent order outbox (D20.1, D12.2). Sending and status
 * queries are backlog MOB-08; this port only stores requests and their states.
 */
export interface OrderOutbox {
  listDrafts(): Promise<readonly Draft[]>
  listRequests(): Promise<readonly MobileRequest[]>
  getRequest(requestId: string): Promise<MobileRequest | null>
  /**
   * Turns a Draft into a `Pending` request in one transaction: the Draft is
   * removed and the request added, or nothing changes. Fails if the
   * `requestId` already exists.
   */
  submitDraft(key: DraftKey, request: MobileRequest): Promise<void>
  /** Stores a new state of an existing request; fails for an unknown `requestId`. */
  updateRequest(request: MobileRequest): Promise<void>
  /** Marks Drafts older than the retention period `ExpiredNeedsReview`; returns how many changed. */
  expireDrafts(now: Date): Promise<number>
  /** Called after every stored change, so counts shown elsewhere stay current. */
  subscribe(listener: () => void): () => void
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
  readonly menu: MenuGateway
  readonly drafts: DraftRepository
  readonly syncStatus: SyncStatusSource
  readonly deviceStore: DeviceStore
}
