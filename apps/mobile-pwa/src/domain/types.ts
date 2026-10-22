/**
 * Mobile-side view models. Values are produced by the Branch Service (or a
 * fixture adapter standing in for it); the Mobile client never derives
 * business results such as totals or table-session transitions itself
 * (requirements D10.1, D17).
 */

/** Whether the Branch Service is reachable over the shop LAN. */
export type BranchConnection = 'online' | 'offline'

/** Device record returned after a successful QR pairing (requirement 3.3 / D11). */
export interface PairedDevice {
  readonly deviceId: string
  readonly deviceName: string
  readonly branchId: string
  readonly branchName: string
  /** Canonical HTTPS IP origin of the Branch Service taken from the pairing QR code. */
  readonly branchServiceUrl: string
  readonly pairedAt: string
}

/** Employee authenticated on a paired device. Device and employee authentication are separate. */
export interface EmployeeSession {
  readonly employeeId: string
  readonly displayName: string
  readonly authenticatedAt: string
  /** Branch session identifier; the session token itself never leaves the auth adapter. */
  readonly sessionId?: string
  readonly expiresAt?: string
}

/** The three device states the Mobile shell distinguishes. */
export type DeviceState =
  | { readonly status: 'unpaired' }
  | { readonly status: 'pairedLoggedOut'; readonly device: PairedDevice }
  | {
      readonly status: 'authenticated'
      readonly device: PairedDevice
      readonly employee: EmployeeSession
    }

/** Table-session states that Mobile may display (requirement D10.1, D20.2). */
export type TableSessionStatus =
  | 'Ordering'
  | 'PaymentPending'
  | 'PaymentInProgress'
  | 'PartiallyPaid'
  | 'Completed'

/** One open table session, as reported by the Branch Service. */
export interface ActiveTable {
  readonly tableId: string
  readonly tableLabel: string
  readonly tableSessionId: string
  readonly status: TableSessionStatus
  readonly tableVersion: number
  readonly openedAt: string
}

export interface ActiveTablesSnapshot {
  readonly tables: readonly ActiveTable[]
  /** When this device received the snapshot from the Branch Service. */
  readonly receivedAt: string
}

/**
 * Counts of locally held Mobile requests by D20.1 state. Requests that reached
 * `BranchAccepted` or `Rejected` are no longer outstanding and are not counted.
 */
export interface OutboxSummary {
  readonly draft: number
  readonly pending: number
  readonly submitted: number
  readonly unknown: number
}

export interface SyncStatus {
  readonly connection: BranchConnection
  /** Last time the Branch Service synchronized employee data, if known (requirement 3.3). */
  readonly employeeDataSyncedAt: string | null
  readonly outbox: OutboxSummary
}

/**
 * Fields of a pairing QR code, version 1 (requirement 3.3 / D11). Names follow
 * the Branch `ParsedBranchPairingQrPayload` record.
 */
export interface PairingQrPayload {
  /** Canonical origin with trailing slash, for example `https://192.0.2.10:8443/`. */
  readonly branchServiceUrl: string
  readonly branchId: string
  readonly pairingId: string
  readonly oneTimeToken: string
}
