import { describe, expect, it } from 'vitest'
import { FIXTURE_PAIRING_PAYLOAD, FIXTURE_PAIRING_QR_TEXT } from '../adapters/fixtures.ts'
import {
  escapeDataString,
  formatPairingQrPayload,
  isCanonicalPairingId,
  isCanonicalPairingToken,
  parsePairingQrPayload,
} from './pairingQr.ts'
import type { PairingQrPayload } from './types.ts'

// Cases mirror tests/Pos.Branch.Api.Tests/BranchPairingQrPayloadFormatTests.cs and
// tests/Pos.Application.Tests/Security/BranchPairingTokenFormatTests.cs.

const SECRET = 'A'.repeat(43)
const PAIRING_ID = 'A'.repeat(22)

function payload(change: Partial<PairingQrPayload> = {}): PairingQrPayload {
  return { ...FIXTURE_PAIRING_PAYLOAD, ...change }
}

describe('formatPairingQrPayload', () => {
  it('formats the canonical version 1 fragment payload', () => {
    expect(FIXTURE_PAIRING_QR_TEXT).toBe(
      `https://192.0.2.10:8443/#pair?v=1&branchId=FIXTURE-BRANCH-01&pairingId=${PAIRING_ID}&token=v1.${SECRET}`,
    )
  })

  it('escapes the branch identifier like .NET Uri.EscapeDataString', () => {
    const text = formatPairingQrPayload(payload({ branchId: 'BRANCH 01/หน้าร้าน' }))
    expect(text).toContain('branchId=BRANCH%2001%2F%E0%B8%AB')
    expect(escapeDataString("a!b'c(d)e*f~g")).toBe('a%21b%27c%28d%29e%2Af~g')
  })
})

describe('parsePairingQrPayload', () => {
  it('round-trips every required pairing field', () => {
    expect(parsePairingQrPayload(FIXTURE_PAIRING_QR_TEXT)).toEqual({
      ok: true,
      payload: FIXTURE_PAIRING_PAYLOAD,
    })
  })

  it('round-trips an escaped non-ASCII branch identifier', () => {
    const expected = payload({ branchId: 'BRANCH 01/หน้าร้าน' })
    expect(parsePairingQrPayload(formatPairingQrPayload(expected))).toEqual({
      ok: true,
      payload: expected,
    })
  })

  it('accepts an IPv4 origin on the default HTTPS port', () => {
    const expected = payload({ branchServiceUrl: 'https://192.0.2.10/' })
    expect(parsePairingQrPayload(formatPairingQrPayload(expected))).toEqual({
      ok: true,
      payload: expected,
    })
  })

  it('accepts a bracketed IPv6 origin', () => {
    const expected = payload({ branchServiceUrl: 'https://[2001:db8::1]:8443/' })
    expect(parsePairingQrPayload(formatPairingQrPayload(expected))).toEqual({
      ok: true,
      payload: expected,
    })
  })

  it.each([
    ['not text', 42],
    ['empty text', ''],
    ['surrounding whitespace', ` ${FIXTURE_PAIRING_QR_TEXT}`],
    ['trailing whitespace', `${FIXTURE_PAIRING_QR_TEXT}\n`],
    ['no fragment', 'https://192.0.2.10:8443/'],
    ['a DNS host name', 'https://branch.pos.internal/#pair?v=1&branchId=x&pairingId=y&token=z'],
    ['a path', 'https://192.0.2.10/pair#pair?v=1&branchId=x&pairingId=y&token=z'],
    ['a query', 'https://192.0.2.10/?token=secret#pair?v=1&branchId=x&pairingId=y&token=z'],
    ['credentials', `https://user@192.0.2.10:8443/#${FIXTURE_PAIRING_QR_TEXT.split('#')[1]}`],
    ['an explicit default port', FIXTURE_PAIRING_QR_TEXT.replace(':8443/', ':443/')],
    ['an upper-case scheme', FIXTURE_PAIRING_QR_TEXT.replace('https://', 'HTTPS://')],
    ['an empty branch identifier', FIXTURE_PAIRING_QR_TEXT.replace('FIXTURE-BRANCH-01', '')],
    ['a padded branch identifier', FIXTURE_PAIRING_QR_TEXT.replace('FIXTURE-BRANCH-01', '%20B')],
    [
      'reordered fields',
      FIXTURE_PAIRING_QR_TEXT.replace(
        `branchId=FIXTURE-BRANCH-01&pairingId=${PAIRING_ID}`,
        `pairingId=${PAIRING_ID}&branchId=FIXTURE-BRANCH-01`,
      ),
    ],
    ['a duplicated field', `${FIXTURE_PAIRING_QR_TEXT}&branchId=FIXTURE-BRANCH-01`],
    [
      'lower-case percent encoding',
      formatPairingQrPayload(payload({ branchId: 'BRANCH/01' })).replace('%2F', '%2f'),
    ],
    ['an invalid token', FIXTURE_PAIRING_QR_TEXT.replace(`v1.${SECRET}`, 'v1.invalid')],
    ['an invalid pairing identifier', FIXTURE_PAIRING_QR_TEXT.replace(PAIRING_ID, 'not-canonical')],
  ])('rejects %s as malformed', (_name, text) => {
    expect(parsePairingQrPayload(text)).toEqual({ ok: false, reason: 'malformed' })
  })

  it('rejects a plain HTTP origin instead of falling back', () => {
    expect(
      parsePairingQrPayload('http://192.0.2.10/#pair?v=1&branchId=x&pairingId=y&token=z'),
    ).toEqual({ ok: false, reason: 'insecureOrigin' })
  })

  it('rejects an unsupported payload version', () => {
    expect(
      parsePairingQrPayload(FIXTURE_PAIRING_QR_TEXT.replace('pair?v=1&', 'pair?v=2&')),
    ).toEqual({ ok: false, reason: 'unsupportedVersion' })
  })
})

describe('pairing identifier and token format', () => {
  it.each([
    ['', false],
    ['short', false],
    [`${'A'.repeat(21)}B`, false], // non-zero trailing bits
    [`${'A'.repeat(21)}!`, false],
    [`${'A'.repeat(21)}=`, false],
    [PAIRING_ID, true],
    [`${'A'.repeat(21)}Q`, true],
  ])('pairing identifier %j is canonical: %s', (value, expected) => {
    expect(isCanonicalPairingId(value)).toBe(expected)
  })

  it.each([
    ['', false],
    ['v1.short', false],
    [`v2.${SECRET}`, false],
    [`V1.${SECRET}`, false],
    [`v1.${'A'.repeat(42)}!`, false],
    [`v1.${'A'.repeat(42)}=`, false],
    [`v1.${'A'.repeat(42)}.`, false],
    [`v1.${'A'.repeat(42)}B`, false], // non-zero trailing bits
    [`v1.${SECRET}`, true],
    [`v1.${'A'.repeat(42)}E`, true],
  ])('token %j is canonical: %s', (value, expected) => {
    expect(isCanonicalPairingToken(value)).toBe(expected)
  })
})
