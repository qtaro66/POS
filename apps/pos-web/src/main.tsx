import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'
import './index.css'
import App from './App.tsx'

const root = createRoot(document.getElementById('root')!)
const preview = import.meta.env.DEV ? new URLSearchParams(window.location.search).get('preview') : null
if (import.meta.env.DEV && preview === 'ordering') {
  void import('./ordering/orderingPreview.ts').then(({ createOrderingPreview }) => {
    const fixture = createOrderingPreview()
    const scenario = new URLSearchParams(window.location.search).get('scenario')
    if (scenario === 'unknown') fixture.state.postBehavior = 'unavailable'
    else if (scenario === 'lost-ack') fixture.state.postBehavior = 'lostAfterAccept'
    else if (scenario === 'rejected') fixture.state.postBehavior = 'reject'
    root.render(<StrictMode><App client={fixture.client} ordering={{ factory: fixture.factory }} branchLabel="สาขาตัวอย่าง 01" preview /></StrictMode>)
  }).catch(() => root.render(<StrictMode><App /></StrictMode>))
} else if (import.meta.env.DEV && preview === 'basket') {
  void Promise.all([import('./ordering/basketPreview.ts'), import('./ordering/BasketPanel.tsx')]).then(async ([fixture, { BasketPanel }]) => {
    const { controller } = await fixture.createBasketPreview()
    root.render(<StrictMode><div className="preview-banner">ตัวอย่างตะกร้า — ข้อมูลจำลอง ไม่ส่งคำสั่งไปสาขาจริง</div><main className="basket-preview"><BasketPanel controller={controller} /></main></StrictMode>)
  }).catch(() => root.render(<StrictMode><App /></StrictMode>))
} else if (import.meta.env.DEV && (preview === 'sent-orders' || preview === 'command-recovery')) {
  void import('./ordering/preview.ts').then(({ createPreviewReadClient, createPreviewRecoverySession }) => {
    root.render(<StrictMode>{preview === 'command-recovery'
      ? <App {...createPreviewRecoverySession()} branchLabel="สาขาตัวอย่าง 01" preview />
      : <App client={createPreviewReadClient()} branchLabel="สาขาตัวอย่าง 01" preview />}</StrictMode>)
  }).catch(() => root.render(<StrictMode><App /></StrictMode>))
} else {
  root.render(<StrictMode><App /></StrictMode>)
}
