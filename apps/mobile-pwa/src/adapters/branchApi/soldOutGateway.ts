import type { DeviceStore, SoldOutGateway } from '../ports.ts'
import type { EmployeeSession, PairedDevice } from '../../domain/types.ts'
import { branchUrl, DEFAULT_BRANCH_TIMEOUT_MS, type FetchLike } from './http.ts'

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function createSoldOutGateway(fetch: FetchLike, store: DeviceStore, tokenFor: (id: string) => string | null): SoldOutGateway {
  async function request(device: PairedDevice, employee: EmployeeSession, path: string, signal: AbortSignal, body?: unknown) {
    const credential = await store.loadCredential(device.deviceId)
    const token = employee.sessionId ? tokenFor(employee.sessionId) : null
    if (!credential || !token) throw new Error('Branch session required.')
    const timeout = AbortSignal.any([signal, AbortSignal.timeout(DEFAULT_BRANCH_TIMEOUT_MS)])
    timeout.throwIfAborted()
    const response = await fetch(branchUrl(device.branchServiceUrl, '/api/v1/ordering/sold-out' + path), {
      method: body === undefined ? 'GET' : 'POST',
      headers: { Authorization: `Device ${credential}`, 'X-POS-Employee-Session': token,
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'omit', redirect: 'error', cache: 'no-store', signal: timeout,
    })
    const value: unknown = response.ok ? await response.json() : null
    return { status: response.status, value }
  }

  return {
    async read(device, employee, offset, signal) {
      const [list, access] = await Promise.all([
        request(device, employee, `?offset=${offset}`, signal),
        request(device, employee, '/capability', signal).catch(() => null),
      ])
      const value = list.value
      if (list.status !== 200 || !record(value) || value.schemaVersion !== 1 || !Array.isArray(value.items)
        || value.items.length > 25 || (value.nextOffset !== null && (value.items.length !== 25 || value.nextOffset !== offset + 25))) {
        throw new Error('Invalid Sold Out list.')
      }
      const items = value.items.map((item: unknown) => {
        if (!record(item) || typeof item.sellableItemId !== 'string' || !item.sellableItemId
          || (item.displayName !== null && typeof item.displayName !== 'string')) throw new Error('Invalid Sold Out item.')
        return { sellableItemId: item.sellableItemId, displayName: item.displayName }
      })
      return { items, nextOffset: value.nextOffset as number | null, canSetSoldOut: access?.status === 200
        && record(access.value) && access.value.schemaVersion === 1 && access.value.canSetSoldOut === true }
    },
    async adjust(device, employee, adjustment, signal) {
      try {
        const { status, value } = await request(device, employee, '/adjustments', signal, adjustment)
        if (status === 200 && record(value) && value.schemaVersion === 1
          && (value.outcome === 'Applied' || value.outcome === 'Replayed')) return 'saved'
        return [400, 401, 403, 404, 409, 413, 415].includes(status) ? 'rejected' : 'unknown'
      } catch { return 'unknown' }
    },
  }
}
