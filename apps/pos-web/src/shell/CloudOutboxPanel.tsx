import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { createCloudOutboxStatus, type CloudOutboxPort } from './cloudOutboxStatus.ts'
import './CloudOutboxPanel.css'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
const labels = { Pending: 'รอส่ง', Processing: 'อยู่ในกระบวนการส่ง', Failed: 'การส่งครั้งล่าสุดล้มเหลว', Other: 'สถานะอื่น ต้องตรวจสอบ' }
function Stamp({ value }: { value: string }) { return <time dateTime={value}>{clock.format(new Date(value))}</time> }
export function CloudOutboxPanel({ port }: { port: CloudOutboxPort }) {
  const controller = useMemo(() => createCloudOutboxStatus(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const list = useRef<HTMLDivElement>(null), focus = useRef<{ owner: typeof controller } | null>(null)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  useEffect(() => {
    if (state.phase !== 'loading' && focus.current) {
      if (focus.current.owner === controller && state.phase === 'ready') list.current?.focus()
      focus.current = null
    }
  }, [state, controller])
  const page = state.page
  return <section className="cloud-outbox" aria-labelledby="cloud-outbox-title" aria-busy={state.phase === 'loading'}>
    <header><h3 id="cloud-outbox-title">ข้อมูลรอส่วนกลางยืนยันรับ</h3><button className="refresh-button" disabled={state.phase === 'loading'} onClick={() => { void controller.refresh() }}>อ่าน Outbox ใหม่</button></header>
    <p>อ่านอย่างเดียว ไม่ส่งข้อมูล ไม่ Retry หรือ Replay ข้อมูลที่ยังไม่ยืนยันรับยังคงอยู่ใน Outbox หลัก ไม่นับคิวกู้คืน Sequence</p>
    {state.phase === 'unsupported' && <p role="status">ยังไม่เชื่อมต่อข้อมูล Outbox</p>}
    {state.phase === 'loading' && <p role="status">กำลังอ่าน Outbox</p>}
    {state.phase === 'unavailable' && <p role="alert">อ่าน Outbox ไม่ได้ กรุณาอ่านใหม่เพื่อกลับหน้าแรก</p>}
    {page && <>
      <dl className="outbox-counts">{[['ยังไม่ยืนยันรับทั้งหมด', page.summary.unacknowledgedCount], ['รอส่ง', page.summary.pendingCount], ['กำลังดำเนินการ', page.summary.processingCount], ['ล้มเหลว', page.summary.failedCount], ['อื่นๆ', page.summary.otherCount]].map(([name, count]) => <div key={name}><dt>{name}</dt><dd>{count}</dd></div>)}</dl>
      <p>อ่านเมื่อ: <Stamp value={page.observedAt} /> · หน้าที่ {state.pageNumber} · แสดง {page.items.length} รายการ</p>
      <p>เรียงตาม branchSequence จากน้อยไปมาก รายการและจำนวนอาจเปลี่ยนระหว่างหน้า ไม่ยืนยันการเชื่อมต่อส่วนกลางหรือสถานะ Worker</p>
      {page.items.length === 0 && <p role="status">ไม่พบรายการในหน้าที่อ่าน ไม่ใช่การยืนยันว่าส่วนกลางออนไลน์</p>}
      <div ref={list} className="outbox-items" role="region" aria-label="รายการ Outbox" tabIndex={0}>
        {page.items.map(item => <article key={item.outboxId} className="outbox-item" aria-label={`Outbox ${item.outboxId}`}>
          <h4>Sequence {item.branchSequence} · {labels[item.processingStatus]} ({item.processingStatus})</h4>
          <dl><div><dt>Event / คำสั่ง</dt><dd>{item.outboxId} / {item.action}</dd></div><div><dt>Request / Session</dt><dd>{item.requestId} / {item.tableSessionId}</dd></div>
            <div><dt>สร้างเมื่อ</dt><dd><Stamp value={item.createdAt} /></dd></div><div><dt>จำนวนครั้งที่ล้มเหลว</dt><dd>{item.retryCount}</dd></div>
            {item.lastFailedAt && <div><dt>ล้มเหลวล่าสุด</dt><dd><Stamp value={item.lastFailedAt} /></dd></div>}
            {item.nextRetryAt && <div><dt>เวลานัดหมายที่บันทึก</dt><dd><Stamp value={item.nextRetryAt} /> (ไม่ใช่คำสั่ง Retry)</dd></div>}
            {item.leaseExpiresAt && <div><dt>Lease สิ้นสุดตามบันทึก</dt><dd><Stamp value={item.leaseExpiresAt} /></dd></div>}
          </dl>{item.expectedBranchSequence && <p>ส่วนกลางเคยแจ้ง Sequence ที่คาดหวัง: {item.expectedBranchSequence} (ข้อมูลจากความล้มเหลวล่าสุด ไม่สั่ง Replay)</p>}
        </article>)}
      </div>
      {page.nextAfterSequence && <button className="refresh-button" onClick={() => { focus.current = { owner: controller }; void controller.next() }}>Outbox หน้าถัดไป</button>}
    </>}
  </section>
}
