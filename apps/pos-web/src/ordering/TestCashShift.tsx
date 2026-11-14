import { useState, type FormEvent } from 'react'
import type { OrderingReadClient } from './readController.ts'

export function TestCashShift({ run }: { run: NonNullable<OrderingReadClient['testCashCycle']> }) {
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    const form = event.currentTarget
    const data = new FormData(form)
    const pin = String(data.get('pin'))
    const pinInput = form.elements.namedItem('pin') as HTMLInputElement
    pinInput.value = ''
    const operation = data.get('operation') === 'close' ? 'close' : 'open'
    let shift = localStorage.getItem('pos-test-cash-shift')
    if (operation === 'open' && !shift) {
      shift = crypto.randomUUID()
      localStorage.setItem('pos-test-cash-shift', shift)
    }
    if (!shift) { setMessage('ไม่พบรหัสกะทดสอบในเครื่องนี้'); return }
    setBusy(true)
    try {
      const result = await run(operation, shift, Number(data.get('cash')), pin)
      if (!('status' in result) || result.status !== 200) { setMessage('Branch ไม่ยืนยันผล'); return }
      if (operation === 'open') { setMessage('เปิดกะทดสอบแล้ว'); return }
      const close = result.body as { expectedCash: number; countedCash: number; difference: number }
      localStorage.removeItem('pos-test-cash-shift')
      setMessage(`ปิดกะแล้ว · ยอดที่ควรมี ${close.expectedCash} · ยอดนับ ${close.countedCash} · ผลต่าง ${close.difference}`)
    } catch { setMessage('Branch ไม่ยืนยันผล') }
    finally { setBusy(false) }
  }
  return <form onSubmit={event => { void submit(event) }}>
    <h3>กะขายเงินสดทดสอบ</h3>
    <label>คำสั่ง<select name="operation"><option value="open">เปิดกะ</option><option value="close">ปิดกะ</option></select></label>
    <label>เงินตั้งต้นหรือยอดนับจริง<input name="cash" type="number" min={0} step="0.01" required /></label>
    <label>ยืนยัน PIN<input name="pin" type="password" inputMode="numeric" required /></label>
    <button disabled={busy}>บันทึกกะทดสอบ</button>
    <p role="status">{message}</p>
  </form>
}
