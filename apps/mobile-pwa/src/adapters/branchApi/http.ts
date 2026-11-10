import type { DeviceStore } from '../ports.ts'
import type { EmployeeSession, PairedDevice } from '../../domain/types.ts'

/** Minimal subset of `fetch` the adapters use, so tests can inject a fake. */
export type FetchLike = (
  input: string,
  init: RequestInit,
) => Promise<Pick<Response, 'ok' | 'status' | 'json'>>

export type PostJsonResult =
  | { readonly kind: 'response'; readonly status: number; readonly body: unknown }
  | { readonly kind: 'unreachable' }

/** Thrown for responses the client cannot interpret; screens report it as an unexpected error. */
export class BranchProtocolError extends Error {
  constructor(message: string) {
    super(message)
    this.name = 'BranchProtocolError'
  }
}

/** Default request timeout. A transport setting, not a business rule. */
export const DEFAULT_BRANCH_TIMEOUT_MS = 10_000

/**
 * Resolves an API path against the Branch service origin. Only HTTPS is
 * allowed; there is no HTTP fallback (requirement 3.3).
 */
export function branchUrl(serviceOrigin: string, path: string): string {
  const url = new URL(path, serviceOrigin)
  if (url.protocol !== 'https:') {
    throw new BranchProtocolError('The Branch service URL must use HTTPS.')
  }
  return url.href
}

/**
 * POSTs JSON with a timeout. Network failures and timeouts become
 * `unreachable`; every HTTP status is returned to the caller. Requests carry
 * no cookies, are never cached, and never follow redirects. Request and
 * response bodies are never logged because they can hold PINs or credentials.
 */
export async function postJson(
  fetchImpl: FetchLike,
  url: string,
  body: unknown,
  options: { readonly headers?: Record<string, string>; readonly timeoutMs: number },
): Promise<PostJsonResult> {
  const controller = new AbortController()
  const timer = setTimeout(() => controller.abort(), options.timeoutMs)
  let response: Pick<Response, 'ok' | 'status' | 'json'>
  try {
    response = await fetchImpl(url, {
      method: 'POST',
      headers: {
        Accept: 'application/json',
        'Content-Type': 'application/json',
        ...options.headers,
      },
      body: JSON.stringify(body),
      credentials: 'omit',
      cache: 'no-store',
      redirect: 'error',
      signal: controller.signal,
    })
  } catch {
    return { kind: 'unreachable' }
  } finally {
    clearTimeout(timer)
  }

  if (!response.ok) {
    return { kind: 'response', status: response.status, body: null }
  }
  try {
    return { kind: 'response', status: response.status, body: await response.json() }
  } catch {
    throw new BranchProtocolError('The Branch service returned a response that is not JSON.')
  }
}

export type AuthenticatedBranchRequest = (
  device: PairedDevice, employee: EmployeeSession, path: string, signal?: AbortSignal, body?: unknown,
) => Promise<{ status: number; value: unknown }>

export function createAuthenticatedBranchRequest(
  fetch: FetchLike, store: DeviceStore, tokenFor: (id: string) => string | null,
): AuthenticatedBranchRequest {
  return async (device, employee, path, signal, body) => {
    const credential = await store.loadCredential(device.deviceId)
    const token = employee.sessionId ? tokenFor(employee.sessionId) : null
    if (!credential || !token) throw new Error('Branch session required.')
    const timeout = signal ? AbortSignal.any([signal, AbortSignal.timeout(DEFAULT_BRANCH_TIMEOUT_MS)]) : AbortSignal.timeout(DEFAULT_BRANCH_TIMEOUT_MS)
    timeout.throwIfAborted()
    const response = await fetch(branchUrl(device.branchServiceUrl, path), {
      method: body === undefined ? 'GET' : 'POST',
      headers: { Authorization: `Device ${credential}`, 'X-POS-Employee-Session': token,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'omit', redirect: 'error', cache: 'no-store', signal: timeout,
    })
    const value: unknown = response.ok ? await response.json() : null
    return { status: response.status, value }
  }
}
