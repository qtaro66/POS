import type { DeviceState, EmployeeSession, PairedDevice } from './types.ts'

export type DeviceSessionAction =
  | { readonly type: 'paired'; readonly device: PairedDevice }
  | { readonly type: 'authenticated'; readonly employee: EmployeeSession }
  | { readonly type: 'loggedOut' }

export function initialDeviceState(device: PairedDevice | null): DeviceState {
  return device ? { status: 'pairedLoggedOut', device } : { status: 'unpaired' }
}

/**
 * Device state transitions of the Mobile shell. Pairing authenticates the
 * device only; an employee must still log in before any table work
 * (requirement 3.3 / D11, D10.4). Transitions that do not apply to the current
 * state are ignored.
 */
export function deviceSessionReducer(
  state: DeviceState,
  action: DeviceSessionAction,
): DeviceState {
  switch (action.type) {
    case 'paired':
      return state.status === 'unpaired'
        ? { status: 'pairedLoggedOut', device: action.device }
        : state
    case 'authenticated':
      return state.status === 'pairedLoggedOut'
        ? { status: 'authenticated', device: state.device, employee: action.employee }
        : state
    case 'loggedOut':
      return state.status === 'authenticated'
        ? { status: 'pairedLoggedOut', device: state.device }
        : state
  }
}
