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
