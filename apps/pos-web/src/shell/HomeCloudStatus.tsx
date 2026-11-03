import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { createCloudOutboxStatus, type CloudOutboxPort } from './cloudOutboxStatus.ts'
import type { FreshnessState } from './dataFreshness.ts'
import { FreshnessValue } from './DataFreshnessPanel.tsx'
import type { OrderingReadClient } from '../ordering/readController.ts'

export function HomeCloudStatus({ port, freshness, openDetails, refreshPermissions }: {
  port: CloudOutboxPort; freshness: FreshnessState; openDetails: () => void
  refreshPermissions?: OrderingReadClient['refreshEmployeePermissions']
}) {
  const controller = useMemo(() => createCloudOutboxStatus(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  const active = useRef<AbortController | undefined>(undefined)
  const [reading, setReading] = useState<{ source: typeof refreshPermissions; message: string; busy: boolean }>()
  const current = reading?.source === refreshPermissions ? reading : undefined
  useEffect(() => () => { active.current?.abort(); active.current = undefined }, [refreshPermissions])
  async function refresh() {
    if (!refreshPermissions || active.current) return
    const request = new AbortController()
    active.current = request
    setReading({ source: refreshPermissions, message: 'กำลังดึงสิทธิ์พนักงาน', busy: true })
    let message = 'ดึงสิทธิ์พนักงานไม่ได้'
    try {
      const result = await refreshPermissions(request.signal)
      message = { Updated: 'อัปเดตสิทธิ์พนักงานแล้ว', NoUpdate: 'ไม่มีข้อมูลสิทธิ์ชุดใหม่ที่นำมาใช้ได้',
        Busy: 'กำลังดึงสิทธิ์อยู่แล้ว', Unavailable: 'ดึงสิทธิ์พนักงานไม่ได้' }[result]
    } catch { /* Keep the current cache and report failure. */ }
    if (!request.signal.aborted) {
      active.current = undefined
      setReading({ source: refreshPermissions, message, busy: false })
    }
  }
  return <section aria-labelledby="home-cloud-status-title">
    <h3 id="home-cloud-status-title">ข้อมูลส่วนกลาง</h3>
    <dl className="system-readings">
      <div><dt>ซิงก์ข้อมูลพนักงานล่าสุด</dt><dd><FreshnessValue reading={freshness.synced} /></dd></div>
      <div><dt>ส่วนกลางยืนยันรับล่าสุด</dt><dd><FreshnessValue reading={freshness.sent} /></dd></div>
      <div><dt>รายการที่ยังไม่ได้รับการยืนยัน</dt><dd role="status">{state.page ? <>
        <strong>{state.page.summary.unacknowledgedCount}</strong> รายการ · เวลาอ้างอิง <FreshnessValue reading={{ phase: 'ready', timestamp: state.page.observedAt }} />
      </> : ({
        loading: 'กำลังอ่านจำนวนรายการ',
        unavailable: 'อ่านจำนวนรายการไม่ได้',
        unsupported: 'ยังไม่เชื่อมต่อ Outbox',
        ready: 'ยังไม่ทราบจำนวนรายการ',
      })[state.phase]}</dd></div>
    </dl>
    <p>ข้อมูลจากสาขา สถานะการเชื่อมต่อส่วนกลางขณะนี้ยังไม่ทราบ</p>
    {refreshPermissions && <button className="refresh-button" disabled={current?.busy}
      onClick={() => { void refresh() }}>ดึงสิทธิ์พนักงานจากส่วนกลาง</button>}
    {current && <p role="status">{current.message}</p>}
    {port.cloudOutboxStatus && <button className="refresh-button" onClick={openDetails}>เปิดรายละเอียด Outbox</button>}
  </section>
}
