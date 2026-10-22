import { useReducer, useSyncExternalStore } from 'react'
import type { MobileServices } from './adapters/ports.ts'
import { SyncStatusBar } from './components/SyncStatusBar.tsx'
import { deviceSessionReducer, initialDeviceState } from './domain/deviceSession.ts'
import { countUnsent } from './domain/outbox.ts'
import type { EmployeeSession, PairedDevice } from './domain/types.ts'
import { ActiveTablesScreen } from './screens/ActiveTablesScreen.tsx'
import { LoginScreen } from './screens/LoginScreen.tsx'
import { PairingScreen } from './screens/PairingScreen.tsx'
import { text } from './strings.ts'
import './App.css'

/** Mobile POS application shell: unpaired → paired (logged out) → employee authenticated. */
function App({ services }: { readonly services: MobileServices }) {
  const [state, dispatch] = useReducer(deviceSessionReducer, services.deviceStore, (store) =>
    initialDeviceState(store.load()),
  )
  const sync = useSyncExternalStore(
    (listener) => services.syncStatus.subscribe(listener),
    () => services.syncStatus.getStatus(),
  )

  function handlePaired(device: PairedDevice) {
    services.deviceStore.save(device)
    dispatch({ type: 'paired', device })
  }

  function handleAuthenticated(employee: EmployeeSession) {
    dispatch({ type: 'authenticated', employee })
  }

  function handleLogout() {
    if (state.status !== 'authenticated') return
    // Logging out never deletes or blocks on unsent Draft/Pending items (requirement D10.5).
    dispatch({ type: 'loggedOut' })
    // The local logout stands even if notifying the Branch Service fails.
    const { device, employee } = state
    Promise.resolve()
      .then(() => services.auth.logout(device, employee))
      .catch(() => undefined)
  }

  return (
    <div className="app">
      <header className="app__header">
        <span className="app__name">{text.appName}</span>
        {state.status !== 'unpaired' && (
          <span className="app__branch">{state.device.branchName}</span>
        )}
      </header>
      {state.status !== 'unpaired' && <SyncStatusBar source={services.syncStatus} />}

      {state.status === 'unpaired' && (
        <PairingScreen
          scanner={services.qrScanner}
          pairing={services.pairing}
          onPaired={handlePaired}
        />
      )}
      {state.status === 'pairedLoggedOut' && (
        <LoginScreen
          key={state.device.deviceId}
          device={state.device}
          auth={services.auth}
          unsentCount={countUnsent(sync.outbox)}
          onAuthenticated={handleAuthenticated}
        />
      )}
      {state.status === 'authenticated' && (
        <ActiveTablesScreen
          device={state.device}
          employee={state.employee}
          tables={services.tables}
          onLogout={handleLogout}
        />
      )}
    </div>
  )
}

export default App
