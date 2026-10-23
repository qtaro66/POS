import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBranchServices } from './adapters/branchApi/createBranchServices.ts'
import { createFixtureServices } from './adapters/fixtures.ts'
import { withPersistentOrders } from './adapters/persistentOrders.ts'
import type { MobileServices } from './adapters/ports.ts'
import './index.css'
import App from './App.tsx'

// Adapters are chosen explicitly at build time. Fixtures stay the default until the
// Branch Service can serve this app from its own origin (or allows it through CORS).
const baseServices: MobileServices =
  import.meta.env.VITE_MOBILE_ADAPTERS === 'branch'
    ? createBranchServices()
    : createFixtureServices()

const now = () => new Date()
const root = createRoot(document.getElementById('root')!)

function render(services: MobileServices, initialDevice: Awaited<ReturnType<MobileServices['deviceStore']['load']>>) {
  root.render(
    <StrictMode>
      <App services={services} initialDevice={initialDevice} now={now} />
    </StrictMode>,
  )
}

async function start() {
  // Drafts and the outbox always live in IndexedDB, in fixture and Branch builds alike (D10.5).
  const services = await withPersistentOrders(baseServices, now)
  // A device store that cannot be read is treated as unpaired, so the user can pair again.
  const device = await services.deviceStore.load().catch(() => null)
  render(services, device)
}

void start()
