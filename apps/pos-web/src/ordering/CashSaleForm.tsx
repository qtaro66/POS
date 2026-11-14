import { useRef, useState, type FormEvent } from 'react'
import type { SessionDetail } from '../api/orderingContracts.ts'
import type { OrderingReadClient } from './readController.ts'

export function CashSaleForm({ detail, send, refresh, simulateReceipt }: {
  detail: SessionDetail
  send: NonNullable<OrderingReadClient['cashStep']>
  refresh: () => void
  simulateReceipt?: OrderingReadClient['simulateReceipt']
}) {
  const pending = useRef<Parameters<typeof send> | undefined>(undefined)
  const lock = useRef(false)
  const [busy, setBusy] = useState(false)
  const [message, setMessage] = useState('')
  const [receiptNumber, setReceiptNumber] = useState<string>()
  const [printResult, setPrintResult] = useState<string>()
  const payment = detail.session.status === 'PaymentPending'
  if (!payment && detail.session.status !== 'Ordering' || detail.hasOpenCancellationRequest) return null

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (lock.current) return
    lock.current = true
    setBusy(true)
    try {
      const tendered = payment ? Number(new FormData(event.currentTarget).get('cash')) : undefined
      const args = pending.current ?? [payment ? 'complete-cash-sale' : 'request-payment',
        detail.session.tableSessionId, detail.session.tableVersion, crypto.randomUUID(), new Date().toISOString(), tendered]
      pending.current = args
      const result = await send(...args)
      if ('status' in result && result.status === 200) {
        pending.current = undefined
        if (payment) {
          const receipt = result.body as { receiptNumber?: string; changeAmount?: number }
          setReceiptNumber(receipt.receiptNumber)
          setMessage('ใบเสร็จทดสอบ ' + receipt.receiptNumber + ' · เงินทอน ' + receipt.changeAmount)
        } else refresh()
      } else {
        setMessage('ยังยืนยันผลไม่ได้ ตรวจสอบสถานะก่อนทำรายการใหม่')
      }
    } finally {
      lock.current = false
      setBusy(false)
    }
  }

  async function simulate(fail: boolean) {
    if (!receiptNumber || !simulateReceipt || lock.current) return
    lock.current = true
    setBusy(true)
    try {
      const result = await simulateReceipt(receiptNumber, fail)
      const body = 'status' in result && result.status === 200 ? result.body as { status?: string; text?: string } : null
      setPrintResult(body?.status === 'Simulated' && typeof body.text === 'string' ? body.text
        : body?.status === 'Failed' ? 'จำลองพิมพ์ล้มเหลว · บิลปิดแล้ว' : 'อ่านผลการพิมพ์จำลองไม่ได้')
    } catch { setPrintResult('อ่านผลการพิมพ์จำลองไม่ได้') }
    finally { lock.current = false; setBusy(false) }
  }

  return <form onSubmit={event => { void submit(event) }}>
    {payment && !receiptNumber && <label>เงินสดที่รับ<input name="cash" type="number" min={0} step="0.01" required disabled={busy} /></label>}
    {!receiptNumber && <button type="submit" disabled={busy}>{payment ? 'ปิดบิลเงินสด (ทดสอบ)' : 'ขอชำระเงิน'}</button>}
    {receiptNumber && simulateReceipt && !printResult && <>
      <button type="button" disabled={busy} onClick={() => { void simulate(false) }}>จำลองพิมพ์สำเร็จ</button>
      <button type="button" disabled={busy} onClick={() => { void simulate(true) }}>จำลองพิมพ์ล้มเหลว</button>
    </>}
    {printResult && <pre role="status">{printResult}</pre>}
    {message && <p role="status">{message}</p>}
  </form>
}
