import type { MenuItem, MenuOption, OptionGroup } from '../../domain/menu.ts'
import type { ActiveTable } from '../../domain/types.ts'
import type { ActiveTablesGateway, MenuGateway } from '../ports.ts'
import type { AuthenticatedBranchRequest } from './http.ts'

function object(value: unknown): Record<string, unknown> {
  if (typeof value !== 'object' || value === null || Array.isArray(value)) throw new Error('Invalid Branch read response.')
  return value as Record<string, unknown>
}

function string(value: unknown): string {
  if (typeof value !== 'string' || !value.trim()) throw new Error('Invalid Branch read text.')
  return value
}

function list(value: unknown): unknown[] {
  if (!Array.isArray(value)) throw new Error('Invalid Branch read list.')
  return value
}

function integer(value: unknown, minimum: number): number {
  if (typeof value !== 'number' || !Number.isSafeInteger(value) || value < minimum) throw new Error('Invalid Branch read quantity.')
  return value
}

function catalogItem(value: unknown): { raw: Record<string, unknown>; option: MenuOption } | null {
  const raw = object(value), flags = object(raw.availability)
  const checks = [flags.existsInActivePackage, flags.centralActive, flags.branchEnabled, flags.hasCurrentPrice]
  if (checks.some(flag => typeof flag !== 'boolean') || typeof flags.soldOut !== 'boolean') throw new Error('Invalid Branch availability.')
  if (checks.some(flag => !flag) || raw.unitPrice === null) return null
  if (typeof raw.unitPrice !== 'number') throw new Error('Invalid Branch price.')
  const price = Math.round(raw.unitPrice * 100)
  if (!Number.isSafeInteger(price) || price < 0 || price / 100 !== raw.unitPrice) throw new Error('Unrepresentable Branch price.')
  return { raw, option: {
    optionId: string(raw.sellableItemId), name: string(raw.displayName), price,
    availability: flags.soldOut ? 'soldOut' : 'available', isDefault: false,
  } }
}

function optionGroup(value: unknown): OptionGroup {
  const raw = object(value), type = raw.optionType
  if ((type !== 'SubItem' && type !== 'Topping') || typeof raw.allowRepeatedOption !== 'boolean') throw new Error('Invalid Branch option group.')
  const minSelections = integer(raw.minSelections, 0)
  const options = list(raw.options).flatMap(candidate => {
    const resolved = catalogItem(candidate)
    if (!resolved) return []
    if (resolved.raw.kind !== type || list(resolved.raw.optionGroups).length) throw new Error('Unsupported Branch option.')
    return [resolved.option]
  })
  return {
    optionGroupId: string(raw.optionGroupId), name: string(raw.displayName), optionType: type,
    minSelections, maxSelections: integer(raw.maxSelections, minSelections), options,
    allowRepeatedOption: raw.allowRepeatedOption, maxQuantityPerOption: integer(raw.maxQuantityPerOption, 1),
  }
}

export function createOrderingReadGateways(request: AuthenticatedBranchRequest): { tables: ActiveTablesGateway; menu: MenuGateway } {
  return {
    tables: { async listActiveTables(device, employee, signal) {
      if (!employee) return { kind: 'unreachable' }
      const response = await request(device, employee, '/api/v1/ordering/table-sessions', signal)
      if (response.status !== 200) return { kind: 'unreachable' }
      const body = object(response.value)
      if (body.schemaVersion !== 1 || body.includesAvailableTables !== false) throw new Error('Invalid active table response.')
      const tables = list(body.sessions).map((candidate): ActiveTable => {
        const row = object(candidate), status = row.status, tableId = string(row.tableId)
        if (status !== 'Ordering' && status !== 'PaymentPending' && status !== 'PaymentInProgress' && status !== 'PartiallyPaid') {
          throw new Error('Invalid active table status.')
        }
        return { tableId, tableLabel: tableId, tableSessionId: string(row.tableSessionId), tableVersion: integer(row.tableVersion, 1), status, openedAt: null }
      })
      return { kind: 'ok', snapshot: { tables, receivedAt: new Date().toISOString() } }
    } },
    menu: { async loadMenu(device, sessionId, employee, signal) {
      if (!employee) return { kind: 'unreachable' }
      const response = await request(device, employee, `/api/v1/ordering/table-sessions/${encodeURIComponent(sessionId)}/menu`, signal)
      if (response.status !== 200) return { kind: 'unreachable' }
      const body = object(response.value)
      if (body.schemaVersion !== 1 || body.tableSessionId !== sessionId) throw new Error('Invalid Branch menu response.')
      const items = list(body.items).flatMap((candidate): MenuItem[] => {
        const resolved = catalogItem(candidate)
        if (!resolved) return []
        if (resolved.raw.kind !== 'Item') throw new Error('Invalid Branch root item.')
        const { optionId, name, price, availability } = resolved.option
        return [{ sellableItemId: optionId, name, price, availability, code: null, categoryId: null,
          optionGroups: list(resolved.raw.optionGroups).map(optionGroup) }]
      })
      return { kind: 'ok', menu: { masterDataVersion: string(body.masterDataVersion), categories: [], items } }
    } },
  }
}
