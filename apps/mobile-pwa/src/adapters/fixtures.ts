import { formatPairingQrPayload } from '../domain/pairingQr.ts'
import type {
  ActiveTable,
  BranchConnection,
  EmployeeSession,
  OutboxSummary,
  PairedDevice,
  PairingQrPayload,
  SyncStatus,
} from '../domain/types.ts'
import type {
  ActiveTablesOutcome,
  DeviceStore,
  LoginOutcome,
  MobileServices,
  PairingOutcome,
  QrScanResult,
  SyncStatusSource,
} from './ports.ts'

/*
 * Deterministic fixture data for development and tests. Every value is
 * synthetic: 192.0.2.0/24 is the RFC 5737 documentation range, the pairing
 * identifier and token are well-formed but constant placeholders, and no
 * identifier, credential or person below exists outside this file.
 */

export const FIXTURE_PAIRING_PAYLOAD: PairingQrPayload = {
  branchServiceUrl: 'https://192.0.2.10:8443/',
  branchId: 'FIXTURE-BRANCH-01',
  pairingId: 'A'.repeat(22),
  oneTimeToken: `v1.${'A'.repeat(43)}`,
}

/** Canonical QR text for `FIXTURE_PAIRING_PAYLOAD`, as the Branch Service would encode it. */
export const FIXTURE_PAIRING_QR_TEXT = formatPairingQrPayload(FIXTURE_PAIRING_PAYLOAD)

export const FIXTURE_DEVICE: PairedDevice = {
  deviceId: 'fixture-device-001',
  deviceName: 'Fixture handheld 1',
  branchId: FIXTURE_PAIRING_PAYLOAD.branchId,
  branchName: 'Fixture Branch 001',
  branchServiceUrl: FIXTURE_PAIRING_PAYLOAD.branchServiceUrl,
  pairedAt: '2026-01-15T03:00:00.000Z',
}

/** PIN accepted by the fixture auth gateway. Not a real credential. */
export const FIXTURE_ACCEPTED_PIN = '2468'

export const FIXTURE_EMPLOYEE: EmployeeSession = {
  employeeId: 'fixture-employee-001',
  displayName: 'Fixture Employee A',
  authenticatedAt: '2026-01-15T03:05:00.000Z',
}

export const FIXTURE_ACTIVE_TABLES: readonly ActiveTable[] = [
  {
    tableId: 'fixture-table-a1',
    tableLabel: 'A1',
    tableSessionId: 'fixture-session-0001',
    status: 'Ordering',
    tableVersion: 3,
    openedAt: '2026-01-15T02:40:00.000Z',
  },
  {
    tableId: 'fixture-table-a2',
    tableLabel: 'A2',
    tableSessionId: 'fixture-session-0002',
    status: 'PaymentPending',
    tableVersion: 7,
    openedAt: '2026-01-15T02:10:00.000Z',
  },
  {
    tableId: 'fixture-table-b1',
    tableLabel: 'B1',
    tableSessionId: 'fixture-session-0003',
    status: 'PartiallyPaid',
    tableVersion: 11,
    openedAt: '2026-01-15T01:55:00.000Z',
  },
]

export const FIXTURE_TABLES_RECEIVED_AT = '2026-01-15T03:06:00.000Z'
export const FIXTURE_EMPLOYEE_DATA_SYNCED_AT = '2026-01-15T02:58:00.000Z'

export const EMPTY_OUTBOX: OutboxSummary = { draft: 0, pending: 0, submitted: 0, unknown: 0 }

export interface FixtureOptions {
  /** Device already paired at start-up; omit to start unpaired. */
  readonly pairedDevice?: PairedDevice | null
  readonly scanResult?: QrScanResult
  readonly pairingOutcome?: PairingOutcome
  readonly loginUnreachable?: boolean
  readonly tables?: readonly ActiveTable[]
  readonly tablesUnreachable?: boolean
  readonly connection?: BranchConnection
  readonly employeeDataSyncedAt?: string | null
  readonly outbox?: OutboxSummary
}

/** In-memory sync status whose state tests and demos can change. */
export class FixtureSyncStatus implements SyncStatusSource {
  private status: SyncStatus
  private readonly listeners = new Set<() => void>()

  constructor(initial: SyncStatus) {
    this.status = initial
  }

  getStatus(): SyncStatus {
    return this.status
  }

  subscribe(listener: () => void): () => void {
    this.listeners.add(listener)
    return () => {
      this.listeners.delete(listener)
    }
  }

  update(change: Partial<SyncStatus>): void {
    this.status = { ...this.status, ...change }
    for (const listener of this.listeners) listener()
  }
}

/** In-memory device store; a real store must persist the device credential on the device. */
export class MemoryDeviceStore implements DeviceStore {
  private device: PairedDevice | null

  constructor(device: PairedDevice | null = null) {
    this.device = device
  }

  load(): PairedDevice | null {
    return this.device
  }

  save(device: PairedDevice | null): void {
    this.device = device
  }
}

export interface FixtureServices extends MobileServices {
  readonly syncStatus: FixtureSyncStatus
  readonly deviceStore: MemoryDeviceStore
}

export function createFixtureServices(options: FixtureOptions = {}): FixtureServices {
  const syncStatus = new FixtureSyncStatus({
    connection: options.connection ?? 'online',
    employeeDataSyncedAt:
      options.employeeDataSyncedAt === undefined
        ? FIXTURE_EMPLOYEE_DATA_SYNCED_AT
        : options.employeeDataSyncedAt,
    outbox: options.outbox ?? EMPTY_OUTBOX,
  })
  const isOffline = () => syncStatus.getStatus().connection === 'offline'

  return {
    syncStatus,
    deviceStore: new MemoryDeviceStore(options.pairedDevice ?? null),
    qrScanner: {
      scan: () =>
        Promise.resolve(options.scanResult ?? { kind: 'scanned', text: FIXTURE_PAIRING_QR_TEXT }),
    },
    pairing: {
      pair: () =>
        Promise.resolve(options.pairingOutcome ?? { kind: 'paired', device: FIXTURE_DEVICE }),
    },
    auth: {
      login: (_device, pin): Promise<LoginOutcome> => {
        if (options.loginUnreachable || isOffline()) {
          return Promise.resolve({ kind: 'unreachable' })
        }
        return Promise.resolve(
          pin === FIXTURE_ACCEPTED_PIN
            ? { kind: 'authenticated', employee: FIXTURE_EMPLOYEE }
            : { kind: 'rejected' },
        )
      },
      logout: () => Promise.resolve(),
    },
    tables: {
      listActiveTables: (): Promise<ActiveTablesOutcome> => {
        if (options.tablesUnreachable || isOffline()) {
          return Promise.resolve({ kind: 'unreachable' })
        }
        return Promise.resolve({
          kind: 'ok',
          snapshot: {
            tables: options.tables ?? FIXTURE_ACTIVE_TABLES,
            receivedAt: FIXTURE_TABLES_RECEIVED_AT,
          },
        })
      },
    },
  }
}
