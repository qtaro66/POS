import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

const root = createRoot(document.getElementById('root')!)
const preview = import.meta.env.DEV ? new URLSearchParams(window.location.search).get('preview') : null
if (import.meta.env.DEV && (preview === 'sent-orders' || preview === 'command-recovery')) {
  void import('./ordering/preview.ts').then(({ createPreviewReadClient, createPreviewRecoverySession }) => {
    root.render(<StrictMode>{preview === 'command-recovery'
      ? <App {...createPreviewRecoverySession()} branchLabel="สาขาตัวอย่าง 01" preview />
      : <App client={createPreviewReadClient()} branchLabel="สาขาตัวอย่าง 01" preview />}</StrictMode>)
  }).catch(() => root.render(<StrictMode><App /></StrictMode>))
} else {
  root.render(<StrictMode><App /></StrictMode>)
}
