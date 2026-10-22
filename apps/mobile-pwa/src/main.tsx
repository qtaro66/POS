import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import { createFixtureServices } from './adapters/fixtures.ts'
import './index.css'
import App from './App.tsx'

// Phase 0 foundation: only deterministic fixture adapters exist. Replace them with
// Branch API adapters when the Mobile pairing/login contracts are available.
const services = createFixtureServices()

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App services={services} />
  </StrictMode>,
)
