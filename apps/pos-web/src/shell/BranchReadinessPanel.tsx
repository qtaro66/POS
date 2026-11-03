import { useEffect, useState } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })

export function BranchReadinessPanel({ read }: { read: OrderingReadClient['branchReadiness'] }) {
  const [attempt, setAttempt] = useState(0)
  const [observation, setObservation] = useState<{ source: typeof read; value: { checkedAt: string; orderingDatabaseReady: boolean } | null }>()
  const reading = observation?.source === read ? observation?.value : undefined
  useEffect(() => {
    if (!read) return
    const controller = new AbortController()
    void (async () => {
      try {
        const result = await read(controller.signal)
        if (!controller.signal.aborted) setObservation({ source: read, value: result.kind === 'ok' ? result.value : null })
      } catch {
        if (!controller.signal.aborted) setObservation({ source: read, value: null })
      }
    })()
    return () => controller.abort()
  }, [read, attempt])

  return <section aria-label="Branch Service / Database">
    <h3>Branch Service / Database</h3>
    <button className="refresh-button" disabled={!read || reading === undefined} onClick={() => {
      setObservation(undefined)
      setAttempt(attempt + 1)
    }}>ตรวจสอบ Branch ใหม่</button>
    <div role="status">{!read ? 'ยังไม่เชื่อมต่อ' : reading === undefined ? 'กำลังตรวจสอบ' : reading === null ? 'อ่านสถานะไม่ได้' :
      <dl className="system-readings">
        <div><dt>Branch Service</dt><dd>ตอบกลับการตรวจสอบ</dd></div>
        <div><dt>Branch Database (Ordering)</dt><dd>{reading.orderingDatabaseReady ? 'พร้อมอ่านโครงสร้างข้อมูล' : 'ตรวจความพร้อมไม่ผ่าน'}</dd></div>
        <div><dt>เวลาตรวจล่าสุด</dt><dd><time dateTime={reading.checkedAt}>{clock.format(new Date(reading.checkedAt))}</time></dd></div>
      </dl>}</div>
  </section>
}
