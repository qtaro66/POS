import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { createCloudReplayStatus, type CloudReplayPort } from './cloudReplayStatus.ts'
import './CloudReplayPanel.css'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
function Stamp({ value }: { value: string }) { return <time dateTime={value}>{clock.format(new Date(value))}</time> }
export function CloudReplayPanel({ port }: { port: CloudReplayPort }) {
  const controller = useMemo(() => createCloudReplayStatus(port), [port])
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
  return <section className="cloud-replay" aria-labelledby="cloud-replay-title" aria-busy={state.phase === 'loading'}>
    <header><h3 id="cloud-replay-title">คิวกู้คืน Sequence</h3><button className="refresh-button" disabled={state.phase === 'loading'} onClick={() => { void controller.refresh() }}>อ่านคิวกู้คืนใหม่</button></header>
    <p>อ่านอย่างเดียว แสดงคำขอกู้คืนที่ยังไม่เสร็จ แม้ Event เดิมเคยยืนยันรับแล้ว คิวนี้แยกจาก Outbox หลักและไม่รวมยอดเข้าด้วยกัน</p>
    {state.phase === 'unsupported' && <p role="status">ยังไม่เชื่อมต่อคิวกู้คืน</p>}
    {state.phase === 'loading' && <p role="status">กำลังอ่านคิวกู้คืน</p>}
    {state.phase === 'unavailable' && <p role="alert">อ่านคิวกู้คืนไม่ได้ กรุณาอ่านใหม่เพื่อกลับหน้าแรก</p>}
    {page && <>
      <dl className="replay-counts">{[['คำขอที่ยังไม่เสร็จ', page.summary.pendingCount], ['มี Lease ตามบันทึก', page.summary.leasedCount], ['ไม่มี Lease ตามบันทึก', page.summary.unleasedCount], ['เคยล้มเหลวตามตัวนับ', page.summary.withRetriesCount]].map(([name, count]) => <div key={name}><dt>{name}</dt><dd>{count}</dd></div>)}</dl>
      <p>กลุ่มที่เคยล้มเหลวซ้อนกับกลุ่มมี/ไม่มี Lease ไม่ใช่ยอดที่นำมาบวกเพิ่ม และ Lease ไม่ยืนยันว่า Worker กำลังทำงาน</p>
      <p>อ่านเมื่อ: <Stamp value={page.observedAt} /> · หน้าที่ {state.pageNumber} · แสดง {page.items.length} รายการ · ข้อมูลอาจเปลี่ยนระหว่างหน้า</p>
      {page.items.length === 0 && <p role="status">ไม่พบคำขอในหน้าที่อ่าน ไม่ยืนยันว่าระบบ Cloud ทั้งหมดปกติ</p>}
      <div ref={list} className="replay-items" role="region" aria-label="รายการคิวกู้คืน" tabIndex={0}>
        {page.items.map(item => <article key={item.recoveryId} className="replay-item" aria-label={`Recovery ${item.recoveryId}`}>
          <h4>Sequence ที่ต้องการ {item.expectedBranchSequence} · ได้รับ {item.receivedBranchSequence}</h4>
          <dl><div><dt>Recovery / Event</dt><dd>{item.recoveryId} / {item.outboxId}</dd></div><div><dt>Request / Session</dt><dd>{item.requestId} / {item.tableSessionId}</dd></div>
            <div><dt>คำสั่ง / ขอเมื่อ</dt><dd>{item.action} / <Stamp value={item.requestedAt} /></dd></div>
            <div><dt>Lease ตามบันทึก</dt><dd>{item.hasLease ? 'มี Lease (อาจหมดอายุแล้ว)' : 'ไม่มี Lease'}</dd></div>
            <div><dt>จำนวนครั้งที่ล้มเหลว</dt><dd>{item.retryCount}</dd></div>
            {item.lastFailedAt && <div><dt>ล้มเหลวล่าสุด</dt><dd><Stamp value={item.lastFailedAt} /></dd></div>}
            {item.nextRetryAt && <div><dt>เวลานัดหมายที่บันทึก</dt><dd><Stamp value={item.nextRetryAt} /> (ไม่ใช่คำสั่ง Retry)</dd></div>}
            {item.leaseExpiresAt && <div><dt>Lease สิ้นสุดตามบันทึก</dt><dd><Stamp value={item.leaseExpiresAt} /></dd></div>}
          </dl>{item.lastExpectedBranchSequence && <p>คำตอบที่บันทึกล่าสุดคาดหวัง Sequence {item.lastExpectedBranchSequence} ไม่ใช่ผลกู้คืนปัจจุบัน</p>}
        </article>)}
      </div>
      {page.nextAfterSequence && <button className="refresh-button" onClick={() => { focus.current = { owner: controller }; void controller.next() }}>คิวกู้คืนหน้าถัดไป</button>}
    </>}
    <p>หน้านี้ไม่ส่ง Replay ไม่ยืนยันรับ ไม่ยึด/ปล่อย Lease และไม่ลบข้อมูล</p>
  </section>
}
