import { isUtcTimestamp } from './printAttentionContracts.ts'

export interface PrinterInventoryItem {
  readonly printerId: string; readonly displayName: string; readonly productionStationId: string; readonly host: string | null; readonly port: number
  readonly protocol: 'RawTcpEscPos' | 'Ipp' | 'Lpr'; readonly compatibilityProfileId: string; readonly manufacturer: string; readonly model: string
  readonly productCode: string | null; readonly firmwareVersion: string | null; readonly paperWidthMillimeters: number
  readonly statusMode: 'Unsupported' | 'SnmpPrinterMib'; readonly supportsCashDrawer: boolean; readonly isActive: boolean; readonly updatedAt: string
}
export interface PrinterInventoryPage { readonly schemaVersion: 1; readonly observedAt: string; readonly totalCount: number; readonly activeCount: number; readonly items: readonly PrinterInventoryItem[]; readonly nextAfterPrinterId: string | null }
export function isPrinterId(value: unknown): value is string { return typeof value === 'string' && value.length > 0 && value.length <= 128 && value === value.trim() && ![...value].some(character => character.charCodeAt(0) < 32 || character.charCodeAt(0) >= 127 && character.charCodeAt(0) <= 159) }
function record(value: unknown): value is Record<string, unknown> { return value !== null && typeof value === 'object' && !Array.isArray(value) }
function text(value: unknown): value is string { return typeof value === 'string' && value.trim().length > 0 && value.length <= 1024 }
function publicHost(value: unknown): boolean {
  if (value === null) return true
  if (typeof value !== 'string' || !/^[A-Za-z0-9._:%[\]-]{1,253}$/.test(value)) return false
  if (!value.includes(':')) return /^[A-Za-z0-9._-]+$/.test(value)
  const unwrapped = value.startsWith('[') && value.endsWith(']') ? value.slice(1, -1) : value
  const parts = unwrapped.split('%')
  if (parts.length > 2 || parts.length === 2 && !/^[A-Za-z0-9_.-]+$/.test(parts[1])) return false
  try { new URL(`http://[${parts[0]}]/`); return true } catch { return false }
}
/** SQLite BINARY ordering uses UTF-8 bytes, which can differ from UTF-16 string ordering. */
function compareId(left: string, right: string) {
  const a = new TextEncoder().encode(left), b = new TextEncoder().encode(right)
  for (let index = 0; index < Math.min(a.length, b.length); index++) if (a[index] !== b[index]) return a[index] - b[index]
  return a.length - b.length
}
function item(value: unknown): boolean {
  return record(value) && ['printerId', 'productionStationId', 'compatibilityProfileId'].every(key => isPrinterId(value[key]))
    && ['displayName', 'manufacturer', 'model'].every(key => text(value[key])) && ['productCode', 'firmwareVersion'].every(key => value[key] === null || text(value[key]))
    && publicHost(value.host) && Number.isSafeInteger(value.port) && (value.port as number) >= 1 && (value.port as number) <= 65535
    && ['RawTcpEscPos', 'Ipp', 'Lpr'].includes(value.protocol as string) && ['Unsupported', 'SnmpPrinterMib'].includes(value.statusMode as string)
    && Number.isSafeInteger(value.paperWidthMillimeters) && (value.paperWidthMillimeters as number) >= 1
    && typeof value.supportsCashDrawer === 'boolean' && typeof value.isActive === 'boolean' && isUtcTimestamp(value.updatedAt)
}
export function parsePrinterInventoryPage(body: unknown, after?: string): PrinterInventoryPage {
  if (!record(body) || body.schemaVersion !== 1 || !isUtcTimestamp(body.observedAt) || !Number.isSafeInteger(body.totalCount) || (body.totalCount as number) < 0
    || !Number.isSafeInteger(body.activeCount) || (body.activeCount as number) < 0 || (body.activeCount as number) > (body.totalCount as number)
    || !Array.isArray(body.items) || body.items.length > 50 || !body.items.every(item)) throw new Error('Invalid printer inventory response.')
  const items = body.items as PrinterInventoryItem[]
  if ((body.totalCount as number) < items.length || (body.activeCount as number) < items.filter(value => value.isActive).length
    || (body.totalCount as number) - (body.activeCount as number) < items.filter(value => !value.isActive).length
    || items.some((value, index) => compareId(value.printerId, index === 0 ? after ?? '' : items[index - 1].printerId) <= 0)
    || !(body.nextAfterPrinterId === null || isPrinterId(body.nextAfterPrinterId) && body.nextAfterPrinterId === items.at(-1)?.printerId)) throw new Error('Invalid printer inventory page.')
  return { schemaVersion: 1, observedAt: body.observedAt, totalCount: body.totalCount as number, activeCount: body.activeCount as number,
    nextAfterPrinterId: body.nextAfterPrinterId as string | null, items: items.map(value => ({ printerId: value.printerId, displayName: value.displayName,
      productionStationId: value.productionStationId, host: value.host, port: value.port, protocol: value.protocol, compatibilityProfileId: value.compatibilityProfileId,
      manufacturer: value.manufacturer, model: value.model, productCode: value.productCode, firmwareVersion: value.firmwareVersion,
      paperWidthMillimeters: value.paperWidthMillimeters, statusMode: value.statusMode, supportsCashDrawer: value.supportsCashDrawer, isActive: value.isActive, updatedAt: value.updatedAt })) }
}
