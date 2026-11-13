import type { MenuItem, MenuOption, OptionGroup } from '../../domain/menu.ts'
import type { ActiveTable } from '../../domain/types.ts'
import type { ActiveTablesGateway, MenuGateway, TableDetail } from '../ports.ts'
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

function amount(value: unknown): number {
  if (typeof value !== 'number' || !Number.isFinite(value)) throw new Error('Invalid Branch amount.')
  return value
}

function tableDetail(value: unknown, sessionId: string): TableDetail {
  const body = object(value)
  const session = object(body.session)
  if (body.schemaVersion !== 1 || session.tableSessionId !== sessionId ||
      typeof body.hasOpenCancellationRequest !== 'boolean') throw new Error('Invalid Branch table detail.')
  const totals = body.billPreviewTotals === null ? null : object(body.billPreviewTotals)
  return {
    tableVersion: integer(session.tableVersion, 1),
    status: string(session.status),
    hasOpenCancellationRequest: body.hasOpenCancellationRequest,
    lines: list(body.batches).flatMap(batch => list(object(batch).lines).map(candidate => {
      const line = object(candidate)
      return {
        id: string(line.orderLineId), name: string(line.displayName),
        quantity: integer(line.activeQuantity, 0), unitPrice: amount(line.unitPrice),
        discount: amount(line.activeDiscountAmount), total: amount(line.activeNetAmount),
        status: string(line.status), comment: typeof line.comment === 'string' ? line.comment : null,
        options: list(line.options).map(candidate => {
          const option = object(candidate)
          return { name: string(option.displayName), quantity: integer(option.quantityPerItem, 1), unitPrice: amount(option.unitPrice) }
        }),
      }
    })),
    totals: totals && {
      'Items': amount(totals.itemGrossAmount),
      'Line discounts': amount(totals.lineDiscountAmount),
      'Bill discount': amount(totals.billDiscountAmount),
      'Service charge': amount(totals.serviceChargeAmount),
      'VAT included': amount(totals.vatInclusiveAmount),
      'Net total': amount(totals.netTotal),
    },
  }
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
    tables: {
      async loadDetail(device, employee, sessionId, signal) {
        const response = await request(device, employee, `/api/v1/ordering/table-sessions/${encodeURIComponent(sessionId)}`, signal)
        return response.status === 200 ? tableDetail(response.value, sessionId) : null
      },
      async openTable(device, employee, tableId, context) {
        const response = await request(device, employee, '/api/v1/ordering/tables/open', undefined, {
          schemaVersion: 1, tableId, context: { ...context, branchId: device.branchId, deviceId: device.deviceId, employeeId: employee.employeeId },
        })
        if (response.status !== 200) throw new Error('OpenTable was not confirmed.')
        const result = object(object(response.value).result)
        if (result.status !== 'BranchAccepted' || result.requestId !== context.requestId) throw new Error('Invalid OpenTable result.')
        return string(result.tableSessionId)
      },
      async listActiveTables(device, employee, signal) {
      if (!employee) return { kind: 'unreachable' }
      const floor = await request(device, employee, '/api/v1/ordering/floor-plan', signal)
      if (floor.status === 200) {
        const body = object(floor.value)
        if (body.schemaVersion !== 1) throw new Error('Invalid floor plan response.')
        const rows = list(body.tables).map(object)
        const available = rows.filter(row => row.status === 'Available').map(row => ({
          tableId: string(row.tableId), tableLabel: string(row.displayName), zone: row.zone === null ? null : string(row.zone),
        }))
        const tables = rows.filter(row => row.status !== 'Available').map((row): ActiveTable => {
          const status = row.status
          if (status !== 'Ordering' && status !== 'PaymentPending' && status !== 'PaymentInProgress' && status !== 'PartiallyPaid') {
            throw new Error('Invalid active table status.')
          }
          return { tableId: string(row.tableId), tableLabel: string(row.displayName), tableSessionId: string(row.tableSessionId),
            tableVersion: integer(row.tableVersion, 1), status, openedAt: null }
        })
        return { kind: 'ok', snapshot: { tables, available, receivedAt: new Date().toISOString() } }
      }
      if (floor.status !== 503 && floor.status !== 404) return { kind: 'unreachable' }
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
