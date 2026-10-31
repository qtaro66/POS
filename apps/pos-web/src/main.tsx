import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

const root = createRoot(document.getElementById('root')!)
if (import.meta.env.DEV && new URLSearchParams(window.location.search).get('preview') === 'sent-orders') {
  void import('./ordering/preview.ts').then(({ createPreviewReadClient }) => {
    root.render(<StrictMode><App client={createPreviewReadClient()} branchLabel="สาขาตัวอย่าง 01" preview /></StrictMode>)
  }).catch(() => root.render(<StrictMode><App /></StrictMode>))
} else {
  root.render(<StrictMode><App /></StrictMode>)
}
