import { useRef, useState, type FormEvent } from 'react'
import type { OrderingReadClient } from './readController.ts'

type CloseTotals = { expectedCash: number; countedCash: number; difference: number }
const formatClose = (close: CloseTotals) =>
  `ปิดกะแล้ว · ยอดที่ควรมี ${close.expectedCash} · ยอดนับ ${close.countedCash} · ผลต่าง ${close.difference}`

export function TestCashShift({ run, status, storageKey }: {
  run: NonNullable<OrderingReadClient['testCashCycle']>
  storageKey: string
  status?: OrderingReadClient['testCashShiftStatus']
}) {
  const inFlight = useRef(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  async function check() {
    if (!status || inFlight.current) return
    inFlight.current = true
    setBusy(true)
    try {
      const result = await status(localStorage.getItem(storageKey))
      if (!('status' in result) || result.status !== 200) {
        setMessage('ยังตรวจสอบสถานะกะไม่ได้')
        return
      }
      const current = result.body as { activeShiftId: string | null; openingCash: number | null; close: CloseTotals | null }
      if (current.activeShiftId) localStorage.setItem(storageKey, current.activeShiftId)
      else if (current.close) localStorage.removeItem(storageKey)
      const active = current.activeShiftId ? `มีกะเปิดอยู่ · เงินตั้งต้น ${current.openingCash}` : 'ไม่มีกะเปิดอยู่'
      setMessage(current.close ? formatClose(current.close) + ' · ' + active : active)
    } catch { setMessage('ยังตรวจสอบสถานะกะไม่ได้') }
    finally { inFlight.current = false; setBusy(false) }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (inFlight.current) return
    const form = event.currentTarget
    const data = new FormData(form)
    const pin = String(data.get('pin'))
    const pinInput = form.elements.namedItem('pin') as HTMLInputElement
    pinInput.value = ''
    const operation = data.get('operation') === 'close' ? 'close' : 'open'
    let shift = localStorage.getItem(storageKey)
    if (operation === 'open' && !shift) {
      shift = crypto.randomUUID()
      localStorage.setItem(storageKey, shift)
    }
    if (!shift) { setMessage('ไม่พบรหัสกะทดสอบในเครื่องนี้'); return }
    inFlight.current = true
    setBusy(true)
    try {
      const result = await run(operation, shift, Number(data.get('cash')), pin)
      if (!('status' in result) || result.status !== 200) { setMessage('Branch ไม่ยืนยันผล'); return }
      if (operation === 'open') { setMessage('เปิดกะทดสอบแล้ว'); return }
      const close = result.body as { expectedCash: number; countedCash: number; difference: number }
      localStorage.removeItem(storageKey)
      setMessage(formatClose(close))
    } catch { setMessage('Branch ไม่ยืนยันผล') }
    finally { inFlight.current = false; setBusy(false) }
  }
  return <form onSubmit={event => { void submit(event) }}>
    <h3>กะขายเงินสดทดสอบ</h3>
    <label>คำสั่ง<select name="operation"><option value="open">เปิดกะ</option><option value="close">ปิดกะ</option></select></label>
    <label>เงินตั้งต้นหรือยอดนับจริง<input name="cash" type="number" min={0} step="0.01" required /></label>
    <label>ยืนยัน PIN<input name="pin" type="password" inputMode="numeric" required /></label>
    <button disabled={busy}>บันทึกกะทดสอบ</button>
    {status && <button type="button" disabled={busy} onClick={() => { void check() }}>ตรวจสอบสถานะกะ</button>}
    <p role="status">{message}</p>
  </form>
}
