import { useEffect, useMemo, useRef, useSyncExternalStore } from 'react'
import { createPrintAttention, type PrintAttentionPort } from './printAttention.ts'
import './PrintAttentionPanel.css'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
const statuses = { PrintQueued: 'รอส่งพิมพ์', Failed: 'ส่งพิมพ์ไม่สำเร็จ', Unknown: 'ไม่ทราบผลการส่ง', Dispatching: 'การส่งเกินเวลา Lease' }
const reasons = { AwaitingDispatch: 'รอระบบส่งงาน', DispatchBlocked: 'ระบบยังส่งงานนี้ไม่ได้', SendFailed: 'การส่งงานล้มเหลว', SendOutcomeUnknown: 'อาจส่งไปแล้ว ห้ามส่งซ้ำอัตโนมัติ', DispatchLeaseExpired: 'Lease หมดอายุหรือไม่มี Lease ต้องตรวจสอบผลเดิม' }
function Stamp({ value }: { value: string }) { return <time dateTime={value}>{clock.format(new Date(value))}</time> }

export function PrintAttentionPanel({ port }: { port: PrintAttentionPort }) {
  const controller = useMemo(() => createPrintAttention(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const list = useRef<HTMLDivElement>(null), focus = useRef<{ owner: typeof controller } | null>(null)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  useEffect(() => {
    if (state.phase !== 'loading' && focus.current) {
      if (focus.current.owner === controller && state.phase === 'ready') list.current?.focus()
      focus.current = null
    }
  }, [state, controller])
  return <section className="print-attention" aria-labelledby="print-attention-title" aria-busy={state.phase === 'loading'}>
    <header><h3 id="print-attention-title" tabIndex={-1}>งานพิมพ์ที่ต้องตรวจสอบ</h3><button className="refresh-button" onClick={() => { void controller.refresh() }} disabled={state.phase === 'loading'}>อ่านงานพิมพ์ใหม่</button></header>
    <p>อ่านอย่างเดียว การส่งงานสำเร็จไม่ยืนยันว่ากระดาษออกจริง หน้านี้ไม่ส่งงานซ้ำและไม่ติดต่อเครื่องพิมพ์</p>
    {state.phase === 'unsupported' && <p role="status">ยังไม่เชื่อมต่อรายการงานพิมพ์</p>}
    {state.phase === 'loading' && <p role="status">กำลังอ่านงานพิมพ์</p>}
    {state.phase === 'unavailable' && <p role="alert">อ่านงานพิมพ์ไม่ได้ กรุณาอ่านใหม่เพื่อกลับหน้าแรก</p>}
    {state.page && <>
      <p>งานในกลุ่มที่ต้องตรวจสอบทั้งหมด: <strong>{state.page.totalCount}</strong> · หน้าที่ {state.pageNumber} · แสดง {state.page.items.length} รายการ</p>
      <p>เวลาอ้างอิง: <Stamp value={state.page.observedAt} /> · รายการอาจเปลี่ยนระหว่างอ่านหน้า ไม่ใช่ Snapshot ของฐานข้อมูล</p>
      {state.page.items.length === 0 && <p role="status">ไม่พบงานในหน้าที่อ่าน ไม่ใช่การยืนยันว่าเครื่องพิมพ์ทุกเครื่องปกติ</p>}
      <div ref={list} className="print-attention-list" role="region" aria-label="รายการงานพิมพ์ที่ต้องตรวจสอบ" tabIndex={0}>
        {state.page.items.map(item => <article key={item.printJobId} className="print-attention-item" aria-label={`งานพิมพ์ ${item.printJobId}`}>
          <h4>{item.purpose === 'KitchenOrder' ? 'ออร์เดอร์ครัว' : 'ใบแจ้งยอด'} · {statuses[item.status]} ({item.status})</h4>
          <p>{reasons[item.attentionReason]}</p>
          <dl><div><dt>Print Job</dt><dd>{item.printJobId}</dd></div><div><dt>Request / Session</dt><dd>{item.requestId} / {item.tableSessionId}</dd></div>
            <div><dt>เอกสารอ้างอิง</dt><dd>{item.snapshotId}</dd></div><div><dt>สถานี / เครื่องพิมพ์</dt><dd>{item.targetStation} / {item.assignedPrinterId ?? 'ยังไม่ระบุเครื่อง'}</dd></div>
            <div><dt>สร้างเมื่อ</dt><dd><Stamp value={item.createdAt} /></dd></div><div><dt>ผลส่งล่าสุด</dt><dd>{item.latestAttemptStatus ?? 'ยังไม่มีบันทึก'}{item.latestAttemptFinishedAt && <> · <Stamp value={item.latestAttemptFinishedAt} /></>}</dd></div>
            {item.nextRetryAt && <div><dt>เวลานัดหมายจากระบบ</dt><dd><Stamp value={item.nextRetryAt} /> (ไม่ใช่คำสั่งส่งซ้ำ)</dd></div>}</dl>
        </article>)}
      </div>
      {state.page.nextCursor && <button className="refresh-button" onClick={() => { focus.current = { owner: controller }; void controller.next() }}>หน้าถัดไป</button>}
    </>}
  </section>
}
