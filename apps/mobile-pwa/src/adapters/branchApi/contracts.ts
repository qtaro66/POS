/*
 * Hand-typed mirror of the Branch JSON contracts in
 * src/BuildingBlocks/Pos.Contracts/Security (System.Text.Json web defaults,
 * so property names are camelCase).
 *
 * TODO(D17): replace with the OpenAPI-generated TypeScript client once the
 * Branch endpoints are included in the OpenAPI description. Today both
 * endpoints call `.ExcludeFromDescription()`, so they cannot be generated.
 */

export const BRANCH_CONTRACT_SCHEMA_VERSION = 1

/** `POST /api/v1/pairing/redemptions` (anonymous, rate-limited). */
export const PAIRING_REDEMPTION_PATH = 'api/v1/pairing/redemptions'

/** `POST /api/v1/employee-sessions` (requires `Authorization: Device <credential>`). */
export const EMPLOYEE_SESSION_PATH = 'api/v1/employee-sessions'

export const DEVICE_AUTHORIZATION_SCHEME = 'Device'

/** Mirrors `BranchPairingRedemptionRequest`. */
export interface BranchPairingRedemptionRequest {
  readonly schemaVersion: number
  readonly branchId: string
  readonly pairingId: string
  readonly oneTimeToken: string
  readonly deviceDisplayName: string
}

/** Mirrors `BranchPairingRedemptionResponse`. */
export interface BranchPairingRedemptionResponse {
  readonly schemaVersion: number
  readonly deviceId: string
  readonly deviceCredential: string
}

/** Mirrors `BranchEmployeeSessionLoginRequest`. */
export interface BranchEmployeeSessionLoginRequest {
  readonly schemaVersion: number
  readonly pin: string
}

/** Mirrors `BranchEmployeeSessionLoginResponse`. */
export interface BranchEmployeeSessionLoginResponse {
  readonly schemaVersion: number
  readonly employeeId: string
  readonly sessionId: string
  readonly sessionToken: string
  readonly expiresAt: string
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null
}

function isNonEmptyString(value: unknown): value is string {
  return typeof value === 'string' && value.length > 0
}

export function readPairingRedemptionResponse(
  value: unknown,
): BranchPairingRedemptionResponse | null {
  if (
    !isRecord(value) ||
    value.schemaVersion !== BRANCH_CONTRACT_SCHEMA_VERSION ||
    !isNonEmptyString(value.deviceId) ||
    !isNonEmptyString(value.deviceCredential)
  ) {
    return null
  }
  return {
    schemaVersion: value.schemaVersion,
    deviceId: value.deviceId,
    deviceCredential: value.deviceCredential,
  }
}

export function readEmployeeSessionLoginResponse(
  value: unknown,
): BranchEmployeeSessionLoginResponse | null {
  if (
    !isRecord(value) ||
    value.schemaVersion !== BRANCH_CONTRACT_SCHEMA_VERSION ||
    !isNonEmptyString(value.employeeId) ||
    !isNonEmptyString(value.sessionId) ||
    !isNonEmptyString(value.sessionToken) ||
    !isNonEmptyString(value.expiresAt) ||
    Number.isNaN(Date.parse(value.expiresAt))
  ) {
    return null
  }
  return {
    schemaVersion: value.schemaVersion,
    employeeId: value.employeeId,
    sessionId: value.sessionId,
    sessionToken: value.sessionToken,
    expiresAt: value.expiresAt,
  }
}
