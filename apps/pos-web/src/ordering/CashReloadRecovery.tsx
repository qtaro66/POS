import { useState } from 'react'
import type { OrderingReadClient } from './readController.ts'

type Send = NonNullable<OrderingReadClient['cashStep']>
type CashArgs = Parameters<Send>

export function cashRecovery(storage: Storage, identity: readonly string[], send: Send) {
  const key = 'pos-pending-cash:' + JSON.stringify(identity)
  function pending(): CashArgs | undefined {
    const raw = storage.getItem(key)
    if (raw === null) return undefined
    const args: unknown = JSON.parse(raw)
    if (!Array.isArray(args) || args.length !== 6 || args[0] !== 'complete-cash-sale' ||
        typeof args[1] !== 'string' || !Number.isSafeInteger(args[2]) ||
        typeof args[3] !== 'string' || typeof args[4] !== 'string' ||
        typeof args[5] !== 'number' || !Number.isFinite(args[5])) {
      throw new Error('Invalid stored cash request')
    }
    return args as CashArgs
  }
  async function submit(...args: CashArgs) {
    if (args[0] !== 'complete-cash-sale') return send(...args)
    const existing = pending()
    if (existing && JSON.stringify(existing) !== JSON.stringify(args)) {
      throw new Error('Recover the existing cash request first')
    }
    storage.setItem(key, JSON.stringify(args))
    const result = await send(...args)
    if ('status' in result && (result.status === 200 || result.status === 409) && storage.getItem(key) === JSON.stringify(args)) storage.removeItem(key)
    return result
  }
  return { pending, submit }
}

export function CashReloadRecovery({ recovery, refresh }: {
  recovery: ReturnType<typeof cashRecovery>
  refresh: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  let pending
  try { pending = recovery.pending() } catch {
    return <p role="alert">อ่านคำขอรับเงินค้างไม่ได้</p>
  }
  if (!pending && !message) return null

  async function recover(args: CashArgs) {
    setBusy(true)
    try {
      const result = await recovery.submit(...args)
      if ('status' in result && result.status === 200) {
        const receipt = result.body as { receiptNumber: string; changeAmount: number }
        setMessage('ใบเสร็จทดสอบ ' + receipt.receiptNumber + ' · เงินทอน ' + receipt.changeAmount)
        refresh()
      } else {
        setMessage('ยังยืนยันผลไม่ได้ ตรวจสอบสถานะก่อนทำรายการใหม่')
      }
    } catch {
      setMessage('ยังยืนยันผลไม่ได้ ตรวจสอบสถานะก่อนทำรายการใหม่')
    } finally {
      setBusy(false)
    }
  }

  return <section aria-label="กู้ผลรับเงิน">
    {pending && <button disabled={busy} onClick={() => { void recover(pending) }}>กู้ผลรับเงินจากคำขอเดิม</button>}
    {message && <p role="status">{message}</p>}
  </section>
}
