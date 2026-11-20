import { useEffect, useState } from 'react'
import type { ClosingReportQueueStatus } from '../api/closingReportContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'

type Reader = OrderingReadClient['closingReportQueueStatus']
type Snapshot = { scopeKey: string; reader: Reader; refreshKey: number; revision: number; value: ClosingReportQueueStatus | null }

export function ClosingReportQueue({ read, scopeKey, refreshKey }: { read: Reader; scopeKey: string; refreshKey: number }) {
  const [revision, setRevision] = useState(0)
  const [snapshot, setSnapshot] = useState<Snapshot | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) {
      void read(abort.signal).then(result => {
        if (!abort.signal.aborted) setSnapshot({ scopeKey, reader: read, refreshKey, revision, value: result.kind === 'ok' ? result.value : null })
      }).catch(() => {
        if (!abort.signal.aborted) setSnapshot({ scopeKey, reader: read, refreshKey, revision, value: null })
      })
    }
    return () => abort.abort()
  }, [read, scopeKey, refreshKey, revision])
  const current = snapshot?.scopeKey === scopeKey && snapshot.reader === read && snapshot.refreshKey === refreshKey && snapshot.revision === revision
  const value = current ? snapshot.value : null
  const loading = !!read && !current
  return <section className="panel store-state" aria-label="รายงานปิดกะและปิดร้านรอส่ง">
    <header className="page-header"><h3>รายงานปิดกะและปิดร้านรอส่ง</h3>
      <button className="refresh-button" disabled={!read || loading} onClick={() => setRevision(previous => previous + 1)}>ตรวจสอบคิวรายงาน</button></header>
    {loading ? <p>กำลังอ่านคิวรายงาน</p> : !value ? <p>ยังอ่านคิวรายงานไม่ได้ ไม่สามารถยืนยันสถานะการส่งได้</p> : <>
      <dl><div><dt>รอการยืนยันจากส่วนกลาง (รวมกำลังส่ง)</dt><dd>{value.pendingCount} รายงาน</dd></div>
        <div><dt>กำลังส่ง</dt><dd>{value.leasedCount} รายงาน</dd></div>
        <div><dt>ส่วนกลางยืนยันแล้ว</dt><dd>{value.deliveredCount} รายงาน</dd></div>
        <div><dt>ยืนยันล่าสุด</dt><dd>{value.lastDeliveredAt ? <time dateTime={value.lastDeliveredAt}>{new Date(value.lastDeliveredAt).toLocaleString('th-TH')}</time> : 'ยังไม่มีการยืนยัน'}</dd></div></dl>
      <p>การส่งรายงานไม่ขัดขวางการปิดกะหรือปิดร้าน คิวว่างไม่ได้ยืนยันว่าส่วนกลางพร้อมใช้งาน</p>
    </>}
  </section>
}
