import type { PairingQrPayload } from './types.ts'

/*
 * Mobile-side reader for the Branch pairing QR payload. It mirrors the Branch
 * contract in `src/Branch/Pos.Branch.Api/Security/BranchPairingQrPayloadFormat.cs`
 * (payload version 1), `BranchPairingPhaseZeroServiceOrigin.cs` and
 * `src/BuildingBlocks/Pos.Application/Security/BranchPairingTokenFormat.cs`:
 *
 *   https://<IP origin>/#pair?v=1&branchId=<escaped>&pairingId=<id>&token=v1.<secret>
 *
 * A payload is accepted only when it is byte-for-byte equal to the canonical
 * form rebuilt from its parts. Any change to the Branch format must be made
 * there first and mirrored here.
 */

export const PAIRING_QR_VERSION = 1

const FRAGMENT_PREFIX = `pair?v=${PAIRING_QR_VERSION}&branchId=`
const PAIRING_ID_SEPARATOR = '&pairingId='
const TOKEN_SEPARATOR = '&token='

const TOKEN_VERSION = 'v1'
const PAIRING_ID_LENGTH = 22 // 16 bytes, base64url without padding
const TOKEN_SECRET_LENGTH = 43 // 32 bytes, base64url without padding
const TOKEN_LENGTH = TOKEN_VERSION.length + 1 + TOKEN_SECRET_LENGTH

const BASE64URL_ALPHABET = 'ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_'
const BASE64URL_PATTERN = /^[A-Za-z0-9_-]*$/
const IPV4_PATTERN = /^\d{1,3}(?:\.\d{1,3}){3}$/

export type PairingQrRejection = 'malformed' | 'insecureOrigin' | 'unsupportedVersion'

export type PairingQrParseResult =
  | { readonly ok: true; readonly payload: PairingQrPayload }
  | { readonly ok: false; readonly reason: PairingQrRejection }

/**
 * Base64url text whose unused trailing bits are zero, so that decoding and
 * re-encoding reproduces it exactly (the Branch canonical-encoding check).
 */
function isCanonicalBase64Url(value: string, length: number): boolean {
  if (value.length !== length || !BASE64URL_PATTERN.test(value)) return false
  const unusedBits = (length * 6) % 8
  const lastIndex = BASE64URL_ALPHABET.indexOf(value[length - 1])
  return (lastIndex & ((1 << unusedBits) - 1)) === 0
}

export function isCanonicalPairingId(value: string): boolean {
  return isCanonicalBase64Url(value, PAIRING_ID_LENGTH)
}

export function isCanonicalPairingToken(value: string): boolean {
  return (
    value.length === TOKEN_LENGTH &&
    value.startsWith(`${TOKEN_VERSION}.`) &&
    isCanonicalBase64Url(value.slice(TOKEN_VERSION.length + 1), TOKEN_SECRET_LENGTH)
  )
}

/** Equivalent of .NET `Uri.EscapeDataString`: escapes everything except RFC 3986 unreserved characters. */
export function escapeDataString(value: string): string {
  return encodeURIComponent(value).replace(
    /[!'()*]/g,
    (character) => `%${character.charCodeAt(0).toString(16).toUpperCase()}`,
  )
}

function isCanonicalBranchId(value: string): boolean {
  return value.trim().length > 0 && value === value.trim()
}

/**
 * Phase 0 Branch service origin: absolute HTTPS URL with an IPv4 or IPv6 host,
 * no credentials, path, query or fragment (requirement 3.3 / D11). Returns the
 * canonical origin with a trailing slash, or the rejection reason.
 */
function parseServiceOrigin(value: string): string | 'malformed' | 'insecureOrigin' {
  let url: URL
  try {
    url = new URL(value)
  } catch {
    return 'malformed'
  }
  if (url.protocol !== 'https:') return 'insecureOrigin'
  const isIpHost = IPV4_PATTERN.test(url.hostname) || url.hostname.startsWith('[')
  if (
    url.username !== '' ||
    url.password !== '' ||
    url.search !== '' ||
    url.hash !== '' ||
    url.pathname !== '/' ||
    url.port === '0' ||
    !isIpHost
  ) {
    return 'malformed'
  }
  return `${url.origin}/`
}

/** Builds the canonical payload text. Used by fixtures and tests; the Branch Service issues real codes. */
export function formatPairingQrPayload(payload: PairingQrPayload): string {
  return (
    `${payload.branchServiceUrl}#${FRAGMENT_PREFIX}${escapeDataString(payload.branchId)}` +
    `${PAIRING_ID_SEPARATOR}${payload.pairingId}${TOKEN_SEPARATOR}${payload.oneTimeToken}`
  )
}

/** Parses scanned QR text following `BranchPairingQrPayloadFormat.TryParse`. */
export function parsePairingQrPayload(text: unknown): PairingQrParseResult {
  const reject = (reason: PairingQrRejection): PairingQrParseResult => ({ ok: false, reason })

  if (typeof text !== 'string' || text.length === 0 || text !== text.trim()) {
    return reject('malformed')
  }
  const hashIndex = text.indexOf('#')
  if (hashIndex < 0 || hashIndex === text.length - 1) return reject('malformed')

  const origin = parseServiceOrigin(text.slice(0, hashIndex))
  if (origin === 'malformed' || origin === 'insecureOrigin') return reject(origin)

  const fragment = text.slice(hashIndex + 1)
  if (!fragment.startsWith(FRAGMENT_PREFIX)) {
    const version = /^pair\?v=(\d+)&/.exec(fragment)?.[1]
    const otherVersion = version !== undefined && Number(version) !== PAIRING_QR_VERSION
    return reject(otherVersion ? 'unsupportedVersion' : 'malformed')
  }

  const branchStart = FRAGMENT_PREFIX.length
  const pairingSeparator = fragment.indexOf(PAIRING_ID_SEPARATOR, branchStart)
  if (pairingSeparator <= branchStart) return reject('malformed')
  const pairingStart = pairingSeparator + PAIRING_ID_SEPARATOR.length
  const tokenSeparator = fragment.indexOf(TOKEN_SEPARATOR, pairingStart)
  if (
    tokenSeparator <= pairingStart ||
    fragment.indexOf('&', tokenSeparator + TOKEN_SEPARATOR.length) >= 0
  ) {
    return reject('malformed')
  }

  const encodedBranchId = fragment.slice(branchStart, pairingSeparator)
  const pairingId = fragment.slice(pairingStart, tokenSeparator)
  const oneTimeToken = fragment.slice(tokenSeparator + TOKEN_SEPARATOR.length)

  let branchId: string
  try {
    branchId = decodeURIComponent(encodedBranchId)
    if (escapeDataString(branchId) !== encodedBranchId) return reject('malformed')
  } catch {
    return reject('malformed')
  }

  if (
    !isCanonicalBranchId(branchId) ||
    !isCanonicalPairingId(pairingId) ||
    !isCanonicalPairingToken(oneTimeToken)
  ) {
    return reject('malformed')
  }

  const payload: PairingQrPayload = { branchServiceUrl: origin, branchId, pairingId, oneTimeToken }
  if (formatPairingQrPayload(payload) !== text) return reject('malformed')
  return { ok: true, payload }
}
