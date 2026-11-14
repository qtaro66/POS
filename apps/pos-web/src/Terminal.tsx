import { useEffect, useState, type FormEvent } from 'react'
import App from './App.tsx'
import { createOrderingClient } from './api/orderingClient.ts'
import { BranchEmployeeAuthGateway, BranchPairingGateway } from '../../mobile-pwa/src/adapters/branchApi/branchGateways.ts'
import { IndexedDbDeviceStore } from '../../mobile-pwa/src/adapters/indexedDbDeviceStore.ts'
import { parsePairingQrPayload } from '../../mobile-pwa/src/domain/pairingQr.ts'
import type { PairedDevice } from '../../mobile-pwa/src/domain/types.ts'

const deviceStore = new IndexedDbDeviceStore(indexedDB, 'pos-pc-device')
const options = { deviceStore, fetch: (url: string, init: RequestInit) => fetch(url, init) }
const auth = new BranchEmployeeAuthGateway(options)
const pairing = new BranchPairingGateway(options)

export default function Terminal() {
  const [device, setDevice] = useState<PairedDevice | null>()
  const [session, setSession] = useState<{ client: ReturnType<typeof createOrderingClient>; close: () => void }>()
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState('')
  useEffect(() => { void deviceStore.load().then(setDevice, () => setError('อ่านข้อมูลเครื่องไม่ได้')) }, [])

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    const form = event.currentTarget
    const data = new FormData(form)
    form.reset()
    setBusy(true)
    setError('')
    try {
      if (!device) {
        const parsed = parsePairingQrPayload(data.get('pairing'))
        if (!parsed.ok) throw new Error()
        const result = await pairing.pair(parsed.payload, 'PC POS')
        if (result.kind !== 'paired') throw new Error()
        setDevice(result.device)
      } else {
        const result = await auth.login(device, String(data.get('pin')))
        if (result.kind !== 'authenticated') throw new Error()
        const employee = result.employee
        const credential = await deviceStore.loadCredential(device.deviceId)
        const token = employee.sessionId && auth.sessionTokenFor(employee.sessionId)
        const expiry = Date.parse(employee.expiresAt ?? '')
        if (!credential || !token || !Number.isFinite(expiry) || expiry <= Date.now()) {
          auth.discardExpiredSession(employee)
          throw new Error()
        }
        const lifetime = new AbortController()
        const client = createOrderingClient({ serviceOrigin: device.branchServiceUrl, branchId: device.branchId,
          deviceId: device.deviceId, employeeId: employee.employeeId, deviceCredential: credential, employeeSessionToken: token }, {
          fetch: (input, init) => fetch(input, { ...init, signal: init?.signal
            ? AbortSignal.any([init.signal, lifetime.signal]) : lifetime.signal }),
        })
        const timer = window.setTimeout(() => close(), expiry - Date.now())
        function close() {
          lifetime.abort()
          window.clearTimeout(timer)
          auth.discardExpiredSession(employee)
          setSession(undefined)
        }
        setSession({ client, close })
      }
    } catch {
      setError(device ? 'เข้าสู่ระบบไม่ได้' : 'จับคู่เครื่องไม่ได้')
    } finally {
      setBusy(false)
    }
  }

  if (session && device) return <App client={session.client} ordering={{ factory: indexedDB }} branchLabel={device.branchName} onLock={session.close} />
  return <main className="pos-locked"><h1>PC POS</h1>
    {device !== undefined && <form onSubmit={event => { void submit(event) }}>
      {device ? <label>PIN<input name="pin" type="password" inputMode="numeric" required /></label>
        : <label>ลิงก์จับคู่เครื่อง<input name="pairing" required /></label>}
      <button disabled={busy}>{device ? 'เข้าสู่ระบบ' : 'จับคู่เครื่อง'}</button>
    </form>}
    {error && <p role="alert">{error}</p>}
  </main>
}
