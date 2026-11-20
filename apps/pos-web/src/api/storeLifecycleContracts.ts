export type StoreOperation = 'OpenStore' | 'OpenShift' | 'CloseShift' | 'CloseStore'
export interface StoreLifecycleCommand {
  requestId: string
  storeSessionId: string
  shiftId: string
  operation: StoreOperation
  cashAmount: number | null
}
export interface StoreLifecycleState {
  storeSessionId: string
  activeShiftId: string | null
  isClosed: boolean
  businessDate: string
  openingCashAmount: number
  closing: null | {
    storeSessionId: string
    shiftId: string
    closesStore: boolean
    expectedCashAmount: number
    actualCashAmount: number | null
    cashDifference: number | null
    reconciliationStatus: string
    context: { employeeId: string; deviceId: string; occurredAt: string; branchLocalDate: string }
  }
}
const object = (value: unknown): value is Record<string, unknown> => value !== null && typeof value === 'object' && !Array.isArray(value)
const money = (value: unknown): value is number => typeof value === 'number' && Number.isFinite(value)
const identifier = (value: unknown): value is string => typeof value === 'string' && value.length > 0 && value.length <= 128

export function parseStoreLifecycle(value: unknown): StoreLifecycleState {
  if (!object(value) || !identifier(value.storeSessionId) ||
      !(value.activeShiftId === null || identifier(value.activeShiftId)) || typeof value.isClosed !== 'boolean' ||
      typeof value.businessDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value.businessDate) ||
      !money(value.openingCashAmount) || value.openingCashAmount < 0 ||
      (value.isClosed && value.activeShiftId !== null)) throw new Error('Invalid store state.')
  if (value.closing !== null) {
    const close = value.closing
    if (!object(close) || close.storeSessionId !== value.storeSessionId || !identifier(close.shiftId) ||
        typeof close.closesStore !== 'boolean' || !money(close.expectedCashAmount) ||
        !(close.actualCashAmount === null || money(close.actualCashAmount)) ||
        !(close.cashDifference === null || money(close.cashDifference)) ||
        !['NotReconciled', 'Reconciled'].includes(close.reconciliationStatus as string) ||
        !object(close.context) || !identifier(close.context.employeeId) || !identifier(close.context.deviceId) ||
        typeof close.context.occurredAt !== 'string' || !Number.isFinite(Date.parse(close.context.occurredAt)) ||
        typeof close.context.branchLocalDate !== 'string') throw new Error('Invalid closing state.')
  }
  return value as unknown as StoreLifecycleState
}

export interface StoreLifecycleRecovery {
  status: 'Completed' | 'Retired' | 'NotFound'
  result: StoreLifecycleState | null
}

export function parseStoreLifecycleRecovery(value: unknown, command: StoreLifecycleCommand): StoreLifecycleRecovery {
  if (!object(value) || !['Completed', 'Retired', 'NotFound'].includes(value.status as string)) throw new Error('Invalid recovery result.')
  if (value.status !== 'Completed') {
    if (value.result !== null) throw new Error('Unexpected recovery state.')
    return { status: value.status as 'Retired' | 'NotFound', result: null }
  }
  const state = parseStoreLifecycle(value.result)
  if (state.storeSessionId !== command.storeSessionId) throw new Error('Different recovery store.')
  if (command.operation === 'OpenStore' || command.operation === 'OpenShift') {
    if (state.isClosed || state.activeShiftId !== command.shiftId ||
        command.operation === 'OpenStore' && state.openingCashAmount !== command.cashAmount) throw new Error('Different recovered shift.')
  } else if (state.activeShiftId !== null || state.isClosed !== (command.operation === 'CloseStore') ||
      state.closing?.shiftId !== command.shiftId || state.closing.closesStore !== (command.operation === 'CloseStore') ||
      state.closing.actualCashAmount !== command.cashAmount) {
    throw new Error('Different recovered closing.')
  }
  return { status: 'Completed', result: state }
}

export interface StoreOperationContext {
  employeeId: string
  deviceId: string
  occurredAt: string
  branchLocalDate: string
}
export interface StoreCashTotals {
  receiptCount: number
  appliedCashAmount: number
  financials?: StoreBillTotals | null
}
export interface StoreBillTotals {
  itemGrossAmount: number
  lineDiscountAmount: number
  billDiscountAmount: number
  serviceChargeAmount: number
  totalBeforeRounding: number
  roundingAdjustment: number
  amountDue: number
  vatInclusiveAmount: number
  amountBeforeVat: number
}
export interface StoreCashSummary {
  source: 'CashSales'
  store: StoreCashTotals
  shift: StoreCashTotals | null
  tables?: StoreTableTotals | null
}
export interface StoreTableTotals {
  openTableCount: number
  awaitingPaymentTableCount: number
  partiallyPaidTableCount: number
}
export interface StoreShiftEntry {
  cashSummary?: StoreCashSummary | null
  storeSessionId: string
  shiftId: string
  openingCashAmount: number | null
  opened: StoreOperationContext
  closing: StoreLifecycleState['closing']
}
export interface StoreWorkspace {
  tables?: StoreTableTotals | null
  cashSummary?: StoreCashSummary | null
  branchId: string
  businessDate: string
  asOf: string
  current: StoreLifecycleState | null
  stores: {
    storeSessionId: string
    businessDate: string
    openingCashAmount: number
    opened: StoreOperationContext
    isClosed: boolean
    shifts: StoreShiftEntry[]
  }[]
}

export function parseStoreCashSummary(value: unknown): StoreCashSummary {
  if (!object(value) || value.source !== 'CashSales') throw new Error('Invalid cash summary source.')
  if (value.tables !== undefined && value.tables !== null) parseStoreTableTotals(value.tables)
  function totals(entry: unknown): StoreCashTotals {
    if (!object(entry) || !Number.isSafeInteger(entry.receiptCount) || (entry.receiptCount as number) < 0 ||
        !money(entry.appliedCashAmount) || entry.appliedCashAmount < 0) throw new Error('Invalid cash summary totals.')
    if (entry.financials !== undefined && entry.financials !== null) {
      const bill = entry.financials
      const fields = ['itemGrossAmount', 'lineDiscountAmount', 'billDiscountAmount', 'serviceChargeAmount',
        'totalBeforeRounding', 'roundingAdjustment', 'amountDue', 'vatInclusiveAmount', 'amountBeforeVat']
      if (!object(bill) || fields.some(field => !money(bill[field]) || (field !== 'roundingAdjustment' && (bill[field] as number) < 0)) ||
          (bill.roundingAdjustment as number) > 0 || bill.amountDue !== entry.appliedCashAmount) throw new Error('Invalid captured bill totals.')
    }
    const result: StoreCashTotals = { receiptCount: entry.receiptCount as number, appliedCashAmount: entry.appliedCashAmount }
    if (entry.financials !== undefined) result.financials = entry.financials as StoreBillTotals | null
    return result
  }
  const result: StoreCashSummary = { source: 'CashSales', store: totals(value.store), shift: value.shift === null ? null : totals(value.shift) }
  if (value.tables !== undefined) result.tables = value.tables as StoreTableTotals | null
  return result
}
export function parseStoreTableTotals(value: unknown): StoreTableTotals {
  if (!object(value) || ![value.openTableCount, value.awaitingPaymentTableCount, value.partiallyPaidTableCount]
    .every(count => Number.isSafeInteger(count) && (count as number) >= 0) ||
    (value.partiallyPaidTableCount as number) > (value.awaitingPaymentTableCount as number) ||
    (value.awaitingPaymentTableCount as number) > (value.openTableCount as number)) throw new Error('Invalid table totals.')
  return value as unknown as StoreTableTotals
}
export function readClosingBlockedTables(value: unknown): { tableSessionId: string; tableId: string }[] | null {
  if (!object(value) || value.error !== 'StoreLifecyclePartialPayments' || !Array.isArray(value.tables) ||
    value.tables.length === 0 || !value.tables.every(table => object(table) && identifier(table.tableSessionId) && identifier(table.tableId))) return null
  return value.tables as { tableSessionId: string; tableId: string }[]
}
function parseOperationContext(value: unknown): StoreOperationContext {
  if (!object(value) || !identifier(value.employeeId) || !identifier(value.deviceId) ||
      typeof value.occurredAt !== 'string' || !/(Z|\+00:00)$/.test(value.occurredAt) || !Number.isFinite(Date.parse(value.occurredAt)) ||
      typeof value.branchLocalDate !== 'string' || !/^\d{4}-\d{2}-\d{2}$/.test(value.branchLocalDate)) {
    throw new Error('Invalid shift attribution.')
  }
  return value as unknown as StoreOperationContext
}

export function parseStoreShiftEntry(value: unknown, storeSessionId: string, shiftId?: string): StoreShiftEntry {
  if (!object(value) || value.storeSessionId !== storeSessionId || !identifier(value.shiftId) ||
      shiftId !== undefined && value.shiftId !== shiftId ||
      !(value.openingCashAmount === null || money(value.openingCashAmount) && value.openingCashAmount >= 0)) {
    throw new Error('Invalid shift entry.')
  }
  if (value.cashSummary !== undefined && value.cashSummary !== null) parseStoreCashSummary(value.cashSummary)
  const opened = parseOperationContext(value.opened)
  if (value.closing !== null) {
    const state = parseStoreLifecycle({
      storeSessionId, activeShiftId: null, isClosed: false, businessDate: opened.branchLocalDate,
      openingCashAmount: 0, closing: value.closing,
    })
    if (state.closing!.shiftId !== value.shiftId) {
      throw new Error('Invalid saved closing.')
    }
  }
  return { ...value, opened } as unknown as StoreShiftEntry
}

export function parseStoreWorkspace(value: unknown, branchId: string): StoreWorkspace {
  if (!object(value) || value.branchId !== branchId || typeof value.businessDate !== 'string' ||
      !/^\d{4}-\d{2}-\d{2}$/.test(value.businessDate) ||
      typeof value.asOf !== 'string' || !/(Z|\+00:00)$/.test(value.asOf) || !Number.isFinite(Date.parse(value.asOf)) ||
      !Array.isArray(value.stores)) throw new Error('Invalid store workspace.')
  if (value.cashSummary !== undefined && value.cashSummary !== null) parseStoreCashSummary(value.cashSummary)
  if (value.tables !== undefined && value.tables !== null) parseStoreTableTotals(value.tables)
  const current = value.current === null ? null : parseStoreLifecycle(value.current)
  const storeIds = new Set<string>()
  const stores = value.stores.map((store: unknown) => {
    if (!object(store) || !identifier(store.storeSessionId) || storeIds.has(store.storeSessionId) ||
        store.businessDate !== value.businessDate || typeof store.isClosed !== 'boolean' ||
        !money(store.openingCashAmount) || store.openingCashAmount < 0 || !Array.isArray(store.shifts)) {
      throw new Error('Invalid workspace store.')
    }
    storeIds.add(store.storeSessionId)
    const opened = parseOperationContext(store.opened)
    const shiftIds = new Set<string>()
    const shifts = store.shifts.map((entry: unknown) => {
      const shift = parseStoreShiftEntry(entry, store.storeSessionId as string)
      if (shiftIds.has(shift.shiftId) || shift.opened.branchLocalDate !== store.businessDate) throw new Error('Invalid workspace shift.')
      shiftIds.add(shift.shiftId)
      return shift
    })
    return { ...store, opened, shifts }
  })
  return { ...value, current, stores } as unknown as StoreWorkspace
}
