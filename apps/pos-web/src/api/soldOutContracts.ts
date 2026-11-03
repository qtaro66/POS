import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface SoldOutItem {
  readonly sellableItemId: string
  readonly displayName: string | null
  readonly stationId: string | null
  readonly changedByEmployeeId: string
  readonly changedAt: string
}

export interface SoldOutPage {
  readonly totalCount: number
  readonly nextOffset: number | null
  readonly items: readonly SoldOutItem[]
}

function record(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value)
}

function item(value: unknown): value is SoldOutItem {
  return record(value) && typeof value.sellableItemId === 'string' && value.sellableItemId.length > 0
    && (value.displayName === null || typeof value.displayName === 'string')
    && (value.stationId === null || typeof value.stationId === 'string')
    && typeof value.changedByEmployeeId === 'string' && isUtcTimestamp(value.changedAt)
}

export function parseSoldOutPage(value: unknown, offset: number): SoldOutPage | null {
  if (!record(value) || value.schemaVersion !== 1 || typeof value.totalCount !== 'number' || !Number.isSafeInteger(value.totalCount)
    || value.totalCount < 0 || !Array.isArray(value.items) || value.items.length > 25 || !value.items.every(item)) return null
  const next = offset + value.items.length
  const nextOffset = next < value.totalCount ? next : null
  if (value.items.length !== Math.min(25, Math.max(0, value.totalCount - offset))
    || value.nextOffset !== nextOffset
    || new Set(value.items.map(row => row.sellableItemId)).size !== value.items.length) return null
  return { totalCount: value.totalCount, nextOffset, items: value.items }
}
