import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createBranchServices } from './adapters/branchApi/createBranchServices.ts'
import { createFixtureServices } from './adapters/fixtures.ts'
import type { MobileServices } from './adapters/ports.ts'
import './index.css'
import App from './App.tsx'

// Adapters are chosen explicitly at build time. Fixtures stay the default until the
// Branch Service can serve this app from its own origin (or allows it through CORS).
const services: MobileServices =
  import.meta.env.VITE_MOBILE_ADAPTERS === 'branch'
    ? createBranchServices()
    : createFixtureServices()

const root = createRoot(document.getElementById('root')!)

function render(initialDevice: Awaited<ReturnType<MobileServices['deviceStore']['load']>>) {
  root.render(
    <StrictMode>
      <App services={services} initialDevice={initialDevice} />
    </StrictMode>,
  )
}

// A device store that cannot be read is treated as unpaired, so the user can pair again.
services.deviceStore.load().then(render, () => render(null))
