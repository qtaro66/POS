import { useEffect, useReducer, useSyncExternalStore } from 'react'
import type { MobileServices } from './adapters/ports.ts'
import { SyncStatusBar } from './components/SyncStatusBar.tsx'
import { useSessionExpiry } from './components/useSessionExpiry.ts'
import { deviceSessionReducer, initialDeviceState } from './domain/deviceSession.ts'
import { countUnsent } from './domain/outbox.ts'
import { isSessionExpired } from './domain/sessionExpiry.ts'
import type { EmployeeSession, PairedDevice } from './domain/types.ts'
import { AuthenticatedShell } from './screens/AuthenticatedShell.tsx'
import { LoginScreen } from './screens/LoginScreen.tsx'
import { PairingScreen } from './screens/PairingScreen.tsx'
import { text } from './strings.ts'
import './App.css'

const systemNow = () => new Date()

/** Mobile POS application shell: unpaired → paired (logged out) → employee authenticated. */
function App({
  services,
  initialDevice,
  now = systemNow,
}: {
  readonly services: MobileServices
  /** Device restored from the device store before the first render. */
  readonly initialDevice: PairedDevice | null
  /** Clock used for session expiry; replaced in tests. Must keep the same identity. */
  readonly now?: () => Date
}) {
  const [state, dispatch] = useReducer(deviceSessionReducer, initialDevice, initialDeviceState)
  const sync = useSyncExternalStore(
    (listener) => services.syncStatus.subscribe(listener),
    () => services.syncStatus.getStatus(),
  )

  // Authenticated screens name themselves in the title; outside them the app name is used.
  useEffect(() => {
    if (state.status !== 'authenticated') document.title = text.appName
  }, [state.status])

  // Expiry only drops the in-memory session: pairing, Draft and outbox data stay on the device.
  function expireSession(employee: EmployeeSession) {
    services.auth.discardExpiredSession(employee)
    dispatch({ type: 'sessionExpired' })
  }

  useSessionExpiry(state.status === 'authenticated' ? state.employee : null, now, () => {
    if (state.status === 'authenticated') expireSession(state.employee)
  })

  // The pairing gateway has already stored the device and its credential.
  function handlePaired(device: PairedDevice) {
    dispatch({ type: 'paired', device })
  }

  function handleAuthenticated(employee: EmployeeSession) {
    // A session that is already expired (for example because of clock skew) is never used.
    if (isSessionExpired(employee, now())) {
      expireSession(employee)
      return
    }
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
        <div className="app__identity">
          <span className="app__name">{text.appName}</span>
          {state.status !== 'unpaired' && (
            <span className="app__branch">{state.device.branchName}</span>
          )}
        </div>
        {state.status === 'authenticated' && (
          <button type="button" className="button button--compact" onClick={handleLogout}>
            {text.logout}
          </button>
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
          notice={state.reason === 'sessionExpired' ? text.sessionExpired : null}
          onAuthenticated={handleAuthenticated}
        />
      )}
      {state.status === 'authenticated' && (
        <AuthenticatedShell
          device={state.device}
          employee={state.employee}
          services={services}
          now={now}
        />
      )}
    </div>
  )
}

export default App
