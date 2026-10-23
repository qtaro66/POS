import type { EmployeeSession, PairedDevice, PairingQrPayload } from '../../domain/types.ts'
import type {
  DeviceStore,
  EmployeeAuthGateway,
  LoginOutcome,
  PairingGateway,
  PairingOutcome,
} from '../ports.ts'
import {
  BRANCH_CONTRACT_SCHEMA_VERSION,
  DEVICE_AUTHORIZATION_SCHEME,
  EMPLOYEE_SESSION_PATH,
  PAIRING_REDEMPTION_PATH,
  readEmployeeSessionLoginResponse,
  readPairingRedemptionResponse,
  type BranchEmployeeSessionLoginRequest,
  type BranchPairingRedemptionRequest,
} from './contracts.ts'
import {
  BranchProtocolError,
  DEFAULT_BRANCH_TIMEOUT_MS,
  branchUrl,
  postJson,
  type FetchLike,
} from './http.ts'

export interface BranchGatewayOptions {
  readonly fetch: FetchLike
  readonly deviceStore: DeviceStore
  readonly timeoutMs?: number
  readonly now?: () => Date
}

/**
 * Pairing through `POST /api/v1/pairing/redemptions`. The Branch Service
 * answers 401 for every rejected code, so expired, used and unknown codes
 * cannot be told apart and are all reported as `invalid`.
 */
export class BranchPairingGateway implements PairingGateway {
  private readonly options: BranchGatewayOptions

  constructor(options: BranchGatewayOptions) {
    this.options = options
  }

  async pair(payload: PairingQrPayload, deviceDisplayName: string): Promise<PairingOutcome> {
    const request: BranchPairingRedemptionRequest = {
      schemaVersion: BRANCH_CONTRACT_SCHEMA_VERSION,
      branchId: payload.branchId,
      pairingId: payload.pairingId,
      oneTimeToken: payload.oneTimeToken,
      deviceDisplayName,
    }
    const result = await postJson(
      this.options.fetch,
      branchUrl(payload.branchServiceUrl, PAIRING_REDEMPTION_PATH),
      request,
      { timeoutMs: this.options.timeoutMs ?? DEFAULT_BRANCH_TIMEOUT_MS },
    )
    if (result.kind === 'unreachable') return { kind: 'unreachable' }

    switch (result.status) {
      case 200: {
        const response = readPairingRedemptionResponse(result.body)
        if (!response) {
          throw new BranchProtocolError('The pairing response does not match the contract.')
        }
        const device: PairedDevice = {
          deviceId: response.deviceId,
          deviceName: deviceDisplayName,
          branchId: payload.branchId,
          // The redemption response carries no branch name; the identifier is shown instead.
          branchName: payload.branchId,
          branchServiceUrl: payload.branchServiceUrl,
          pairedAt: (this.options.now?.() ?? new Date()).toISOString(),
        }
        await this.options.deviceStore.save(device, response.deviceCredential)
        return { kind: 'paired', device }
      }
      case 400:
        return { kind: 'rejected', reason: 'invalidRequest' }
      case 401:
        return { kind: 'rejected', reason: 'invalid' }
      case 429:
        return { kind: 'rejected', reason: 'rateLimited' }
      default:
        throw new BranchProtocolError(`Unexpected pairing status ${result.status}.`)
    }
  }
}

interface HeldSessionToken {
  readonly token: string
  /** Branch-provided expiry in epoch milliseconds. */
  readonly expiresAtMs: number
}

/**
 * Employee PIN login through `POST /api/v1/employee-sessions`, authenticated
 * with the stored device credential. The session token stays in this
 * object's memory only, so reloading the app requires a new login. A token is
 * never handed out at or after its Branch-provided `expiresAt`.
 */
export class BranchEmployeeAuthGateway implements EmployeeAuthGateway {
  private readonly options: BranchGatewayOptions
  private readonly sessionTokens = new Map<string, HeldSessionToken>()

  constructor(options: BranchGatewayOptions) {
    this.options = options
  }

  async login(device: PairedDevice, pin: string): Promise<LoginOutcome> {
    const credential = await this.options.deviceStore.loadCredential(device.deviceId)
    if (!credential) {
      throw new BranchProtocolError('No device credential is stored for this device.')
    }

    const request: BranchEmployeeSessionLoginRequest = {
      schemaVersion: BRANCH_CONTRACT_SCHEMA_VERSION,
      pin,
    }
    const result = await postJson(
      this.options.fetch,
      branchUrl(device.branchServiceUrl, EMPLOYEE_SESSION_PATH),
      request,
      {
        headers: { Authorization: `${DEVICE_AUTHORIZATION_SCHEME} ${credential}` },
        timeoutMs: this.options.timeoutMs ?? DEFAULT_BRANCH_TIMEOUT_MS,
      },
    )
    if (result.kind === 'unreachable') return { kind: 'unreachable' }

    switch (result.status) {
      case 200: {
        const response = readEmployeeSessionLoginResponse(result.body)
        if (!response) {
          throw new BranchProtocolError('The login response does not match the contract.')
        }
        this.sessionTokens.set(response.sessionId, {
          token: response.sessionToken,
          expiresAtMs: Date.parse(response.expiresAt),
        })
        const employee: EmployeeSession = {
          employeeId: response.employeeId,
          // The login response carries no display name; the employee ID is shown instead.
          displayName: response.employeeId,
          authenticatedAt: (this.options.now?.() ?? new Date()).toISOString(),
          sessionId: response.sessionId,
          expiresAt: response.expiresAt,
        }
        return { kind: 'authenticated', employee }
      }
      case 401:
        // Also returned when the device credential is revoked; the two cannot be told apart.
        return { kind: 'rejected' }
      default:
        throw new BranchProtocolError(`Unexpected login status ${result.status}.`)
    }
  }

  /** No Branch logout endpoint exists yet, so logout only forgets the in-memory session token. */
  logout(_device: PairedDevice, employee: EmployeeSession): Promise<void> {
    if (employee.sessionId) this.sessionTokens.delete(employee.sessionId)
    return Promise.resolve()
  }

  discardExpiredSession(employee: EmployeeSession): void {
    if (employee.sessionId) this.sessionTokens.delete(employee.sessionId)
  }

  /**
   * Session token for later protected requests; never rendered or persisted.
   * Returns null, and forgets the token, once the session has expired.
   */
  sessionTokenFor(sessionId: string): string | null {
    const held = this.sessionTokens.get(sessionId)
    if (!held) return null
    if ((this.options.now?.() ?? new Date()).getTime() >= held.expiresAtMs) {
      this.sessionTokens.delete(sessionId)
      return null
    }
    return held.token
  }
}
