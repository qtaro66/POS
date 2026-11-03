import { useEffect, useMemo, useSyncExternalStore } from 'react'
import { createCloudOutboxStatus, type CloudOutboxPort } from './cloudOutboxStatus.ts'
import type { FreshnessState } from './dataFreshness.ts'
import { FreshnessValue } from './DataFreshnessPanel.tsx'

export function HomeCloudStatus({ port, freshness, openDetails }: {
  port: CloudOutboxPort; freshness: FreshnessState; openDetails: () => void
}) {
  const controller = useMemo(() => createCloudOutboxStatus(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
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
    {port.cloudOutboxStatus && <button className="refresh-button" onClick={openDetails}>เปิดรายละเอียด Outbox</button>}
  </section>
}
