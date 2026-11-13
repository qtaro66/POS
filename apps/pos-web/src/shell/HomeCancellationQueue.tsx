import { useEffect, useState } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import type { CancellationQueueEntry } from '../api/orderingClient.ts'

const labels = {
  CancellationRequested: 'รอตรวจสอบ',
  ProductionStatusChecking: 'กำลังตรวจสอบการผลิต',
  StatusRecorded: 'บันทึกผลตรวจแล้ว',
}

export function HomeCancellationQueue({ read }: { read: NonNullable<OrderingReadClient['cancellationQueue']> }) {
  const [queue, setQueue] = useState<readonly CancellationQueueEntry[] | null>()
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

  return <section aria-labelledby="cancellation-queue-title">
    <h3 id="cancellation-queue-title">คำขอยกเลิกรายการ</h3>
    <button className="refresh-button" onClick={() => { setQueue(undefined); setRefresh(value => value + 1) }}>อ่านคิวใหม่</button>
    <p role="status">{queue === undefined ? 'กำลังอ่านคิว' : queue === null ? 'อ่านคิวไม่ได้' : queue.length ? 'มีคำขอรอดำเนินการ' : 'ไม่มีคำขอค้าง'}</p>
    {!!queue?.length && <ul>{queue.map(entry => <li key={entry.cancellationRequestId}>
      <strong>{entry.tableSessionId}</strong> · {labels[entry.status]}
      <ul>{entry.lines.map(line => <li key={line.orderLineId}>{line.displayName} × {line.requestedQuantity}</li>)}</ul>
    </li>)}</ul>}
  </section>
}
