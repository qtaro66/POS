import { CancellationProductionForm } from './CancellationProductionForm.tsx'
import { useEffect, useState } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import type { CancellationQueueEntry } from '../api/orderingClient.ts'

const labels = {
  CancellationRequested: 'รอตรวจสอบ',
  ProductionStatusChecking: 'กำลังตรวจสอบการผลิต',
  StatusRecorded: 'บันทึกผลตรวจแล้ว',
}

export function HomeCancellationQueue({ read, queueInspection, recordProduction, rejectCancellation, confirmCancellation }: {
  read: NonNullable<OrderingReadClient['cancellationQueue']>
  confirmCancellation?: OrderingReadClient['confirmCancellation']
  rejectCancellation?: OrderingReadClient['rejectCancellation']
  recordProduction?: OrderingReadClient['recordCancellationProduction']
  queueInspection?: OrderingReadClient['queueCancellationInspection']
}) {
  const [queue, setQueue] = useState<readonly CancellationQueueEntry[] | null>()
  const [printing, setPrinting] = useState(false)
  const [printMessage, setPrintMessage] = useState<string>()
  const [refresh, setRefresh] = useState(0)
  useEffect(() => {
    const controller = new AbortController()
    async function load() {
      try {
        const result = await read(controller.signal)
        if (!controller.signal.aborted) setQueue(result.kind === 'ok' ? result.value : null)
      } catch {
        if (!controller.signal.aborted) setQueue(null)
      }
    }
    void load()
    return () => controller.abort()
  }, [read, refresh])

  async function print(id: string) {
    if (!queueInspection || printing) return
    setPrinting(true)
    setPrintMessage(undefined)
    try {
      const result = await queueInspection(id)
      setPrintMessage(result.kind === 'ok' ? 'เพิ่มใบตรวจสอบเข้าคิวพิมพ์แล้ว' : 'ยืนยันการเข้าคิวพิมพ์ไม่ได้')
      setRefresh(value => value + 1)
    } catch {
      setPrintMessage('ยืนยันการเข้าคิวพิมพ์ไม่ได้')
    } finally {
      setPrinting(false)
    }
  }

  async function reject(id: string) {
    if (!rejectCancellation || printing || !window.confirm('ปฏิเสธคำขอยกเลิกและคงรายการอาหารไว้หรือไม่?')) return
    setPrinting(true)
    try {
      const result = await rejectCancellation(id)
      setPrintMessage(result.kind === 'ok' ? 'ปฏิเสธคำขอแล้ว' : 'ยืนยันการปฏิเสธคำขอไม่ได้')
      setRefresh(value => value + 1)
    } catch {
      setPrintMessage('ยืนยันการปฏิเสธคำขอไม่ได้')
    } finally {
      setPrinting(false)
    }
  }

  return <section aria-labelledby="cancellation-queue-title">
    <h3 id="cancellation-queue-title">คำขอยกเลิกรายการ</h3>
    <button className="refresh-button" onClick={() => { setQueue(undefined); setRefresh(value => value + 1) }}>อ่านคิวใหม่</button>
    <p role="status">{queue === undefined ? 'กำลังอ่านคิว' : queue === null ? 'อ่านคิวไม่ได้' : queue.length ? 'มีคำขอรอดำเนินการ' : 'ไม่มีคำขอค้าง'}</p>
    {printMessage && <p role="status">{printMessage}</p>}
    {!!queue?.length && <ul>{queue.map(entry => <li key={entry.cancellationRequestId}>
      {typeof entry.reason === 'string' && <p>เหตุผล: {entry.reason}</p>}
      <strong>{entry.tableSessionId}</strong> · {labels[entry.status]}
      {queueInspection && entry.status === 'CancellationRequested' && entry.lines.length > 0 &&
        <button className="refresh-button" disabled={printing} onClick={() => { void print(entry.cancellationRequestId) }}>
          พิมพ์ใบตรวจสอบ
        </button>}
      {recordProduction && entry.status === 'ProductionStatusChecking' && entry.lines.length > 0 &&
        <CancellationProductionForm entry={entry} record={recordProduction} onSaved={() => {
          setPrintMessage('บันทึกผลตรวจแล้ว')
          setRefresh(value => value + 1)
        }} />}
      {confirmCancellation && entry.canConfirm && entry.status === 'StatusRecorded' && entry.lines.length > 0 &&
        entry.lines.every(line => line.checked) &&
        <CancellationProductionForm key="confirm" entry={entry} record={confirmCancellation} confirming onSaved={() => {
          setPrintMessage('ยืนยันการยกเลิกแล้ว')
          setRefresh(value => value + 1)
        }} />}
      {rejectCancellation && entry.status === 'StatusRecorded' &&
        <button className="refresh-button" disabled={printing} onClick={() => { void reject(entry.cancellationRequestId) }}>
          ปฏิเสธคำขอยกเลิก
        </button>}
      <ul>{entry.lines.map(line => <li key={line.orderLineId}>{line.displayName} × {line.requestedQuantity}</li>)}</ul>
    </li>)}</ul>}
  </section>
}
