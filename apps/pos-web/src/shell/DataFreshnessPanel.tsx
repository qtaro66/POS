import type { FreshnessReading, FreshnessState } from './dataFreshness.ts'
import './DataFreshnessPanel.css'
import { PrintAttentionPanel } from './PrintAttentionPanel.tsx'
import type { PrintAttentionPort } from './printAttention.ts'
import { CloudOutboxPanel } from './CloudOutboxPanel.tsx'
import type { CloudOutboxPort } from './cloudOutboxStatus.ts'
import { CloudReplayPanel } from './CloudReplayPanel.tsx'
import type { CloudReplayPort } from './cloudReplayStatus.ts'
import { PrinterInventoryPanel } from './PrinterInventoryPanel.tsx'
import type { PrinterInventoryPort } from './printerInventory.ts'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
export function FreshnessValue({ reading }: { reading: FreshnessReading }) {
  if (reading.phase === 'ready') return reading.timestamp === null ? <>ยังไม่มีบันทึก</> : <time dateTime={reading.timestamp}>{clock.format(new Date(reading.timestamp))}</time>
  return <>{({ loading: 'กำลังอ่านข้อมูล', unavailable: 'อ่านข้อมูลไม่ได้', unsupported: 'ยังไม่เชื่อมต่อ' })[reading.phase]}</>
}

export function DataFreshnessSidebar({ state }: { state: FreshnessState }) {
  return <section className="data-freshness-sidebar" aria-label="สถานะข้อมูลส่วนกลาง">
    <div><strong>Data Synced</strong><FreshnessValue reading={state.synced} /><small>ข้อมูลพนักงานเท่านั้น</small></div>
    <div><strong>Data Sent</strong><FreshnessValue reading={state.sent} /><small>ส่วนกลางยืนยันรับล่าสุด</small></div>
  </section>
}

export function DataFreshnessPanel({ state, onRefresh, printPort, outboxPort, replayPort, printerPort }: { state: FreshnessState; onRefresh: () => void; printPort?: PrintAttentionPort; outboxPort?: CloudOutboxPort; replayPort?: CloudReplayPort; printerPort?: PrinterInventoryPort }) {
  const loading = state.synced.phase === 'loading' || state.sent.phase === 'loading'
  return <section className="system-status panel" aria-labelledby="system-status-title">
    <header><div><p className="eyebrow">PC POS / สถานะระบบ</p><h2 id="system-status-title">สถานะระบบ</h2></div>
      <button className="refresh-button" onClick={onRefresh} disabled={loading}>อ่านสถานะใหม่</button></header>
    <p>สถานะรวม: ยังไม่ทราบ — ยังไม่มีข้อมูล Health Check ครบทุกระบบ</p>
    <p>แสดงข้อมูลล่าสุดที่อ่านจาก Branch Service ไม่ใช่การยืนยันว่าเชื่อมต่อส่วนกลางอยู่ในขณะนี้</p>
    <dl className="system-readings"><div><dt>Data Synced</dt><dd><FreshnessValue reading={state.synced} /></dd><dd>ครอบคลุมข้อมูลพนักงาน ยังไม่ยืนยันการซิงก์ Master Data ทั้งหมด</dd></div>
      <div><dt>Data Sent</dt><dd><FreshnessValue reading={state.sent} /></dd><dd>เวลาล่าสุดที่บันทึกการยืนยันรับจากส่วนกลาง ไม่ใช่จำนวนรายการค้างส่ง</dd></div></dl>
    <h3>ข้อมูลที่ยังไม่เชื่อมต่อ</h3>
    <ul>{['Branch Service / Database Health Check', 'PrintGateway / สภาพเครื่องพิมพ์', 'Backup / Database Checkpoint / พื้นที่สำรองข้อมูล'].map(label => <li key={label}>{label}: ยังไม่ทราบ</li>)}</ul>
    <p>หน้านี้อ่านอย่างเดียว ไม่มีคำสั่ง Retry, Reprint หรือทดสอบอุปกรณ์</p>
    {printerPort && <PrinterInventoryPanel port={printerPort} />}
    {printPort && <PrintAttentionPanel port={printPort} />}
    {outboxPort && <CloudOutboxPanel port={outboxPort} />}
    {replayPort && <CloudReplayPanel port={replayPort} />}
  </section>
}
