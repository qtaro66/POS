import { useState, type FormEvent } from 'react'
import type { CancellationQueueEntry, ProductionCheck } from '../api/orderingClient.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'

const fields = [
  ['notStarted', 'ยังไม่เริ่ม'], ['inProgress', 'กำลังทำ'],
  ['completed', 'เสร็จแล้ว'], ['notApplicable', 'ไม่มีกระบวนการผลิต'],
] as const

export function CancellationProductionForm({ entry, record, onSaved, confirming = false }: {
  entry: CancellationQueueEntry
  record: NonNullable<OrderingReadClient['recordCancellationProduction' | 'confirmCancellation']>
  confirming?: boolean
  onSaved: () => void
}) {
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string>()
  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (busy) return
    const data = new FormData(event.currentTarget)
    const lines = entry.lines.map((line, index): ProductionCheck => ({
      orderLineId: line.orderLineId,
      notStarted: Number(data.get(index + '-notStarted')),
      inProgress: Number(data.get(index + '-inProgress')),
      completed: Number(data.get(index + '-completed')),
      notApplicable: Number(data.get(index + '-notApplicable')),
    }))
    if (confirming && !lines.some(line => fields.some(([key]) => line[key] > 0))) {
      setError('เลือกจำนวนที่จะยกเลิกอย่างน้อยหนึ่งรายการ')
      return
    }
    if (!confirming && lines.some((line, index) => fields.reduce((sum, [key]) => sum + line[key], 0) !== entry.lines[index].requestedQuantity)) {
      setError('ผลรวมแต่ละรายการต้องเท่ากับจำนวนที่ขอตรวจสอบ')
      return
    }
    setBusy(true)
    setError(undefined)
    try {
      const result = await record(entry.cancellationRequestId, lines)
      if (result.kind === 'ok') onSaved()
      else setError('บันทึกผลตรวจไม่ได้ ตรวจจำนวน ประเภทการผลิต และสถานะคำขอ')
    } catch {
      setError('ยืนยันการบันทึกผลตรวจไม่ได้ ลองส่งข้อมูลเดิมอีกครั้ง')
    } finally {
      setBusy(false)
    }
  }
  return <form onSubmit={event => { void submit(event) }}>
    {entry.lines.map((line, index) => <fieldset key={line.orderLineId} disabled={busy}>
      <legend>{line.displayName} × {line.requestedQuantity}</legend>
      {fields.map(([key, label]) => <label key={key}>{label}
        <input type="number" name={index + '-' + key} defaultValue={0} min={0}
          max={confirming ? line.checked?.[key] ?? 0 : line.requestedQuantity} step={1} required />
      </label>)}
    </fieldset>)}
    {error && <p role="alert">{error}</p>}
    <button className="refresh-button" disabled={busy}>{confirming ? 'ยืนยันการยกเลิก' : 'บันทึกผลตรวจ'}</button>
  </form>
}
