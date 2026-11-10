import type { SoldOutGateway } from '../ports.ts'
import type { AuthenticatedBranchRequest } from './http.ts'

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

export function createSoldOutGateway(request: AuthenticatedBranchRequest): SoldOutGateway {
  return {
    async read(device, employee, offset, signal) {
      const [list, access] = await Promise.all([
        request(device, employee, `/api/v1/ordering/sold-out?offset=${offset}`, signal),
        request(device, employee, '/api/v1/ordering/sold-out/capability', signal).catch(() => null),
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
        const { status, value } = await request(device, employee, '/api/v1/ordering/sold-out/adjustments', signal, adjustment)
        if (status === 200 && record(value) && value.schemaVersion === 1
          && (value.outcome === 'Applied' || value.outcome === 'Replayed')) return 'saved'
        return [400, 401, 403, 404, 409, 413, 415].includes(status) ? 'rejected' : 'unknown'
      } catch { return 'unknown' }
    },
  }
}
