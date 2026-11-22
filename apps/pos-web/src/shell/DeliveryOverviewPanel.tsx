import type { DeliveryChannel, DeliveryOverview } from '../api/deliveryOverviewContracts.ts'
import type { FreshnessReading } from './dataFreshness.ts'

const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'short', timeStyle: 'medium', timeZone: 'Asia/Bangkok' })
function Timestamp({ value }: { value: string | null }) { return value === null ? <>ยังไม่มีบันทึก</> : <time dateTime={value}>{clock.format(new Date(value))}</time> }
const stateLabels = { Pending: 'ยังมีรายการรอยืนยัน', Current: 'รายการในคิวที่อ่านได้รับการยืนยันแล้ว', NoHistory: 'ยังไม่มีประวัติการส่งในคิวที่อ่าน' }
export function DataSentSummary({ reading }: { reading: FreshnessReading }) {
  if (reading.phase !== 'ready') return <span>{({ loading: 'กำลังอ่านข้อมูล', unavailable: 'อ่านข้อมูลไม่ได้', unsupported: 'ยังไม่เชื่อมต่อ' })[reading.phase]}</span>
  if (!reading.overview) return <><Timestamp value={reading.timestamp} /><small>เวลาธุรกรรมโต๊ะเดิมเท่านั้น ยังไม่ครอบคลุมคิวรายงานและงานกู้ลำดับ</small></>
  const view = reading.overview.snapshot
  return <div className={`delivery-summary delivery-${view.state}`}>
    <span role="status">{stateLabels[view.state]}</span>
    <small>ธุรกรรมโต๊ะรอ {view.tableEvents.pendingCount} · รายงานรอ {view.closingReports.pendingCount} · กู้ลำดับรอ {view.replayPendingCount} · บริบทกะรอ {view.lifecycleReports.pendingCount}</small>
    <small>ยืนยันรับล่าสุด <Timestamp value={view.lastConfirmedAt} /></small>
    <small>อ่านคิวเมื่อ <Timestamp value={view.asOf} /></small>
  </div>
}
function Channel({ name, value }: { name: string; value: DeliveryChannel }) {
  return <section aria-label={name}><h4>{name}</h4><dl className="store-attribution">
    <div><dt>ทั้งหมด / ยืนยันแล้ว / รอยืนยัน</dt><dd>{value.totalCount} / {value.deliveredCount} / {value.pendingCount}</dd></div>
    <div><dt>อยู่ระหว่างส่ง</dt><dd>{value.inFlightCount}</dd></div><div><dt>รอถึงเวลาส่งหรือลองใหม่</dt><dd>{value.delayedCount}</dd></div>
    <div><dt>การส่งเดิมหมดช่วงเวลาที่จองไว้</dt><dd>{value.expiredLeaseCount}</dd></div><div><dt>สถานะที่ระบบยังจำแนกไม่ได้</dt><dd>{value.unknownCount}</dd></div>
    <div><dt>รายการรอเก่าสุดสร้างเมื่อ</dt><dd><Timestamp value={value.oldestPendingAt} /></dd></div>
    <div><dt>ยืนยันรับล่าสุดของช่องทาง</dt><dd><Timestamp value={value.lastConfirmedAt} /></dd></div>
  </dl></section>
}
function ConfigurationWarnings({ view }: { view: DeliveryOverview }) {
  const snapshot = view.snapshot
  return <>
    {!view.tableDeliveryEnabled && (snapshot.tableEvents.pendingCount > 0 || snapshot.replayPendingCount > 0) && <p role="status">มีธุรกรรมหรืองานกู้ลำดับรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่งจากฐานข้อมูลสาขานี้</p>}
    {!view.reportDeliveryEnabled && snapshot.closingReports.pendingCount > 0 && <p role="status">มีรายงานรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่งรายงานของสาขานี้</p>}
    {!view.lifecycleDeliveryEnabled && snapshot.lifecycleReports.pendingCount > 0 && <p role="status">มีข้อมูลเปิดร้านและกะรอยืนยัน แต่ยังไม่ได้ตั้งค่าเปิดส่งข้อมูลส่วนนี้</p>}
    <p>การตั้งค่าตัวส่ง: ธุรกรรมโต๊ะ {view.tableDeliveryEnabled ? 'เปิด' : 'ยังไม่เปิด'} · รายงาน {view.reportDeliveryEnabled ? 'เปิด' : 'ยังไม่เปิด'} · บริบทกะ {view.lifecycleDeliveryEnabled ? 'เปิด' : 'ยังไม่เปิด'} — ไม่ใช่การยืนยันว่าส่วนกลางพร้อมใช้งาน</p>
  </>
}
export function DeliveryOverviewPanel({ reading }: { reading: FreshnessReading }) {
  const view = reading.phase === 'ready' ? reading.overview : undefined
  return <section className="delivery-overview panel" aria-label="สรุปการส่งธุรกรรมและรายงาน">
    <h3>สรุปการส่งธุรกรรมและรายงาน</h3><DataSentSummary reading={reading} />
    {view ? <><ConfigurationWarnings view={view} />
      <Channel name="ธุรกรรมโต๊ะ" value={view.snapshot.tableEvents} /><Channel name="รายงานปิดกะและปิดร้าน" value={view.snapshot.closingReports} /><Channel name="ข้อมูลเปิดร้านและกะ" value={view.snapshot.lifecycleReports} />
      <section aria-label="งานกู้ลำดับข้อมูล"><h4>งานกู้ลำดับข้อมูล</h4><dl className="store-attribution">
        <div><dt>รอยืนยัน / ยืนยันแล้ว</dt><dd>{view.snapshot.replayPendingCount} / {view.snapshot.replayCompletedCount}</dd></div>
        <div><dt>งานรอเก่าสุดสร้างเมื่อ</dt><dd><Timestamp value={view.snapshot.replayOldestPendingAt} /></dd></div>
        <div><dt>ยืนยันรับล่าสุดของงานกู้ลำดับ</dt><dd><Timestamp value={view.snapshot.replayLastConfirmedAt} /></dd></div>
      </dl></section></> : <p>ยังยืนยันภาพรวมทุกคิวไม่ได้ ไม่ใช้เวลาธุรกรรมเดิมแทนจำนวนรายงานที่ค้างอยู่</p>}
    <p>เวลาในรายการเป็นเวลาที่บันทึกไว้ รายการอาจเปลี่ยนหลังอ่านข้อมูล และการรอหรือหมดเวลาส่งไม่ได้พิสูจน์ว่าส่วนกลางยังไม่ได้รับข้อมูล</p>
    <p>สรุปนี้ครอบคลุมคิวธุรกรรมโต๊ะ รายงานปิดกะ/ปิดร้าน ข้อมูลเปิดร้านและกะ และงานกู้ลำดับเท่านั้น ไม่ใช่สถานะเครือข่ายหรือการซิงก์ข้อมูลทั้งหมด</p>
  </section>
}
