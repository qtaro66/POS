import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import type { ClosingLifecycleFilter, ClosingReportDelivery, ClosingReportDetail, ClosingReportFailure, ClosingReportFilter, ClosingReportItem, ClosingReportPage, ClosingReportQuery, ClosingReportState } from '../api/closingReportWorkspaceContracts.ts'
import type { StoreCashTotals } from '../api/storeLifecycleContracts.ts'
import './ClosingReportWorkspace.css'
import { ClosingReportLifecycleView } from './ClosingReportLifecycleView.tsx'

type Port = Pick<OrderingReadClient, 'closingReports' | 'closingReportDetail' | 'closingReportDelivery'>
export interface ClosingReportFocus { storeSessionId: string; shiftId: string; revision: number }
const states: Record<ClosingReportState, string> = { Queued: 'รอส่ง', InFlight: 'อยู่ระหว่างดำเนินการส่ง', RetryScheduled: 'รอลองส่งอีกครั้ง', LeaseExpired: 'รอกู้คืนการส่ง', Delivered: 'ส่วนกลางยืนยันแล้ว' }
const failures: Record<ClosingReportFailure, string> = { TransportUnavailable: 'ติดต่อส่วนกลางหรือรับคำตอบไม่สำเร็จ', CentralRejected: 'ส่วนกลางยังไม่ยืนยันรับรายงาน', AcknowledgmentUnavailable: 'ยังไม่ได้รับคำยืนยันที่ตรงกับรายงาน' }
const money = (amount: number) => amount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const date = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
function Time({ value }: { value: string }) { return <time dateTime={value}>{date.format(new Date(value))}</time> }
export function ClosingReportDeliveryStatus({ read }: { read: Port['closingReportDelivery'] }) {
  const [snapshot, setSnapshot] = useState<{ reader: typeof read; value: ClosingReportDelivery | null } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(abort.signal).then(result => {
      if (!abort.signal.aborted) setSnapshot({ reader: read, value: result.kind === 'ok' ? result.value : null })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ reader: read, value: null }) })
    return () => abort.abort()
  }, [read])
  const value = snapshot && snapshot.reader === read ? snapshot.value : null
  return <aside className="closing-report-delivery" aria-label="การส่งรายงานอัตโนมัติ">
    {value ? <><p>{value.deliveryEnabled ? 'ตั้งค่าเปิดส่งรายงานอัตโนมัติสำหรับสาขานี้' : 'ยังไม่ได้เปิดส่งรายงานอัตโนมัติสำหรับสาขานี้'}</p>
      <p>ตรวจสอบการตั้งค่าเมื่อ <Time value={value.asOf} /> · การเปิดตัวส่งไม่ได้ยืนยันว่าส่วนกลางพร้อมใช้งาน</p></> : <p>ยังตรวจสอบการตั้งค่าตัวส่งไม่ได้ ไม่สามารถยืนยันการส่งอัตโนมัติ</p>}
  </aside>
}
function DeliveryDetails({ item }: { item: ClosingReportItem }) {
  return <dl className="store-attribution">
    <div><dt>สถานะการส่งรายงานหลัก</dt><dd>{states[item.deliveryState]}</dd></div><div><dt>จำนวนครั้งที่เริ่มส่งรายงานหลัก</dt><dd>{item.attemptCount} ครั้ง</dd></div>
    {item.lastFailure && <div><dt>ผลจากการลองส่งรายงานหลักครั้งก่อน</dt><dd>{failures[item.lastFailure]}</dd></div>}
    {item.deliveryState === 'RetryScheduled' && <div><dt>เริ่มลองใหม่ได้หลัง</dt><dd><Time value={item.availableAt} /></dd></div>}
    {item.deliveryState === 'LeaseExpired' && <div><dt>การดำเนินการ</dt><dd>รอตัวส่งกู้คืนด้วยรายงานเดิม ผลรับที่ส่วนกลางยังไม่ยืนยัน</dd></div>}
    {item.deliveredAt && <div><dt>รายงานหลักยืนยันรับเมื่อ</dt><dd><Time value={item.deliveredAt} /></dd></div>}
  </dl>
}
function LifecycleDeliveryDetails({ detail }: { detail: ClosingReportDetail }) {
  const delivery = detail.lifecycleDelivery
  return <section aria-label="สถานะส่งข้อมูลเปิดร้านและกะ"><h5>การส่งข้อมูลเปิดร้านและกะ</h5>
    <p>การยืนยันรับรายงานหลักไม่ได้ยืนยันรับข้อมูลเปิดร้านและกะส่วนนี้</p>
    {!delivery ? <p>ยังไม่ทราบสถานะส่งข้อมูลเปิดร้านและกะ</p> : <>
      <p role="status">{delivery.deliveryState === 'Delivered' ? 'ส่วนกลางยืนยันข้อมูลเปิดร้านและกะแล้ว' : 'ข้อมูลเปิดร้านและกะยังรอยืนยันจากส่วนกลาง'}</p>
      <dl className="store-attribution"><div><dt>สถานะข้อมูลเปิดร้านและกะ</dt><dd>{states[delivery.deliveryState]}</dd></div>
        <div><dt>จำนวนครั้งที่เริ่มส่งข้อมูลส่วนนี้</dt><dd>{delivery.attemptCount} ครั้ง</dd></div>
        {delivery.lastFailure && <div><dt>ผลส่งข้อมูลส่วนนี้ครั้งก่อน</dt><dd>{delivery.lastFailure === 'ParentUnavailable' ? 'รอระบบกู้คืนรายงานหลักที่ส่วนกลาง ด้วยข้อมูลเดิม ไม่ต้องปิดร้านซ้ำ' : failures[delivery.lastFailure]}</dd></div>}
        {delivery.deliveryState === 'RetryScheduled' && <div><dt>ส่งข้อมูลส่วนนี้ใหม่ได้หลัง</dt><dd><Time value={delivery.availableAt} /></dd></div>}
        {delivery.deliveredAt && <div><dt>ข้อมูลส่วนนี้ยืนยันรับเมื่อ</dt><dd><Time value={delivery.deliveredAt} /></dd></div>}
      </dl>
      {detail.report.deliveryState !== 'Delivered' && <p>ข้อมูลส่วนนี้รอการยืนยันรายงานหลักก่อนส่ง</p>}
    </>}
  </section>
}
function LifecycleListDelivery({ item }: { item: ClosingReportItem }) {
  const delivery = item.lifecycleDelivery
  return <section aria-label="สถานะข้อมูลเปิดร้านและกะของรายการ"><h5>ข้อมูลเปิดร้านและกะของรายงานนี้</h5>
    {delivery === undefined ? <p>รายการนี้ยังไม่ระบุสถานะส่งข้อมูลเปิดร้านและกะ</p>
      : delivery === null ? <p>รายงานนี้ไม่มีข้อมูลเปิดร้านและกะที่บันทึกไว้ ไม่ถือว่าได้รับการยืนยันหรือเป็นงานค้างส่ง</p>
        : <><dl className="store-attribution">
          <div><dt>สถานะส่งข้อมูลเปิดร้านและกะของรายการ</dt><dd>{states[delivery.deliveryState]}</dd></div>
          <div><dt>จำนวนครั้งที่เริ่มส่งข้อมูลส่วนนี้ของรายการ</dt><dd>{delivery.attemptCount} ครั้ง</dd></div>
          {delivery.lastFailure && <div><dt>ผลส่งข้อมูลส่วนนี้ครั้งก่อนของรายการ</dt><dd>{delivery.lastFailure === 'ParentUnavailable' ? 'รอระบบกู้คืนรายงานหลักที่ส่วนกลาง ด้วยข้อมูลเดิม ไม่ต้องปิดร้านซ้ำ' : failures[delivery.lastFailure]}</dd></div>}
          {delivery.deliveryState === 'RetryScheduled' && <div><dt>ลองส่งข้อมูลส่วนนี้ใหม่ได้หลัง</dt><dd><Time value={delivery.availableAt} /></dd></div>}
          {delivery.deliveryState === 'LeaseExpired' && <div><dt>การกู้คืนข้อมูลส่วนนี้</dt><dd>รอตัวส่งกู้คืนข้อมูลเดิม ยังไม่ยืนยันผลรับที่ส่วนกลาง</dd></div>}
          {delivery.deliveredAt && <div><dt>ข้อมูลส่วนนี้ของรายการยืนยันรับเมื่อ</dt><dd><Time value={delivery.deliveredAt} /></dd></div>}
        </dl>{item.deliveryState !== 'Delivered' && <p>ข้อมูลส่วนนี้รอการยืนยันรายงานหลักก่อนส่ง</p>}</>}
  </section>
}
const financialLabels = { itemGrossAmount: 'ยอดสินค้าก่อนส่วนลด', lineDiscountAmount: 'ส่วนลดรายการ', billDiscountAmount: 'ส่วนลดท้ายบิล', serviceChargeAmount: 'ค่าบริการ', totalBeforeRounding: 'ยอดก่อนปัดเศษ', roundingAdjustment: 'ผลต่างปัดเศษ', amountDue: 'ยอดชำระ', vatInclusiveAmount: 'VAT รวมในราคา', amountBeforeVat: 'ยอดก่อน VAT' }
function Totals({ value, title }: { value: StoreCashTotals; title: string }) {
  return <section aria-label={title}><h5>{title}</h5><dl className="store-attribution">
    <div><dt>ใบเสร็จเงินสด</dt><dd>{value.receiptCount} ใบ</dd></div><div><dt>เงินสดรับจากการขาย</dt><dd>{money(value.appliedCashAmount)} บาท</dd></div>
    {value.financials && (Object.keys(financialLabels) as (keyof typeof financialLabels)[]).map(key => <div key={key}><dt>{financialLabels[key]}</dt><dd>{money(value.financials![key])} บาท</dd></div>)}
  </dl>{!value.financials && <p>ยังไม่มีรายละเอียดภาษีและส่วนลดที่บันทึกไว้ครบ</p>}</section>
}
function Detail({ requestId, read, close }: { requestId: string; read: Port['closingReportDetail']; close: () => void }) {
  const [preview, setPreview] = useState(false)
  const [snapshot, setSnapshot] = useState<{ reader: typeof read; value: ClosingReportDetail | null; missing: boolean } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, abort.signal).then(result => {
      if (!abort.signal.aborted) setSnapshot({ reader: read, value: result.kind === 'ok' ? result.value : null, missing: result.kind === 'notFound' })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ reader: read, value: null, missing: false }) })
    return () => abort.abort()
  }, [read, requestId])
  const current = snapshot?.reader === read ? snapshot : null
  const saved = current?.value?.snapshot
  return <section className="closing-report-detail" aria-label="รายงานปิดที่บันทึกไว้">
    <header className="page-header"><h4>รายงานปิดที่บันทึกไว้</h4><button onClick={close}>ปิดรายละเอียดรายงาน</button></header>
    {!current && read ? <p role="status">กำลังอ่านรายงานที่บันทึกไว้</p> : !saved ? <p role="alert">{current?.missing ? 'ไม่พบรายงานนี้ในสาขา' : 'ยังอ่านรายงานนี้ไม่ได้ กรุณาตรวจสอบรายการอีกครั้ง'}</p> : <>
      <button type="button" aria-pressed={preview} onClick={() => setPreview(value => !value)}>{preview ? 'กลับมุมมองรายละเอียดรายงาน' : 'เปิดตัวอย่างเอกสารรายงาน'}</button>
      <article className={preview ? 'closing-report-document' : undefined} aria-label={preview ? 'ตัวอย่างเอกสารรายงานปิด' : 'รายละเอียดที่บันทึก ณ เวลาปิด'}>
      {preview && <h4>{saved.operation === 'CloseStore' ? 'รายงานปิดร้าน' : 'รายงานปิดกะ'} · {saved.businessDate}</h4>}
      <p>ข้อมูล ณ เวลาปิดที่บันทึกไว้ ไม่คำนวณใหม่จากยอดปัจจุบัน</p>
      <dl className="store-attribution"><div><dt>ประเภทรายงาน</dt><dd>{saved.operation === 'CloseStore' ? 'ปิดร้าน' : 'ปิดกะระหว่างวัน'}</dd></div>
        <div><dt>วันธุรกิจ</dt><dd>{saved.businessDate}</dd></div><div><dt>รอบร้าน</dt><dd>{saved.storeSessionId}</dd></div><div><dt>กะ</dt><dd>{saved.shiftId}</dd></div>
        <div><dt>ผู้ยืนยัน</dt><dd>{saved.closing.context.employeeId}</dd></div><div><dt>เวลาปิด</dt><dd><Time value={saved.closing.context.occurredAt} /></dd></div>
        <div><dt>ยอดที่ควรมี</dt><dd>{money(saved.closing.expectedCashAmount)} บาท</dd></div>
        <div><dt>ยอดนับจริง</dt><dd>{saved.closing.actualCashAmount === null ? 'ไม่ได้กระทบยอด (NotReconciled)' : money(saved.closing.actualCashAmount) + ' บาท'}</dd></div>
        <div><dt>ผลต่าง</dt><dd>{saved.closing.cashDifference === null ? 'ไม่ได้กระทบยอด' : money(saved.closing.cashDifference) + ' บาท'}</dd></div></dl>
      <ClosingReportLifecycleView value={current!.value!.lifecycle ?? null} />
      {saved.cashSummary ? <>
        {saved.cashSummary.shift && <Totals value={saved.cashSummary.shift} title="เงินสดของกะ ณ เวลาปิด" />}
        <Totals value={saved.cashSummary.store} title="เงินสดสะสมรอบร้าน ณ เวลาปิด" />
        {saved.cashSummary.tables ? <section aria-label="โต๊ะ ณ เวลาปิด"><h5>โต๊ะ ณ เวลาปิด</h5><dl className="store-attribution">
          <div><dt>โต๊ะที่ยังไม่ปิด</dt><dd>{saved.cashSummary.tables.openTableCount} โต๊ะ</dd></div><div><dt>โต๊ะรอชำระ รวมชำระบางส่วน</dt><dd>{saved.cashSummary.tables.awaitingPaymentTableCount} โต๊ะ</dd></div>
          <div><dt>โต๊ะชำระบางส่วน</dt><dd>{saved.cashSummary.tables.partiallyPaidTableCount} โต๊ะ</dd></div></dl></section> : <p>ยังไม่มีจำนวนโต๊ะที่บันทึก ณ เวลาปิด</p>}
      </> : <p>รายงานเดิมไม่มีสรุปยอดขายเงินสดและจำนวนโต๊ะที่บันทึกไว้</p>}
      </article>
      {preview && <p>ตัวอย่างเอกสารสำหรับอ่านเท่านั้น ยังไม่สั่งพิมพ์หรือส่งออกไฟล์</p>}
      <section aria-label="สถานะส่งล่าสุดของรายงาน"><DeliveryDetails item={current!.value!.report} /></section>
      <LifecycleDeliveryDetails detail={current!.value!} />
    </>}
  </section>
}
function ReportList({ port, query }: { port: Port; query: ClosingReportQuery }) {
  const read = port.closingReports
  const [snapshot, setSnapshot] = useState<{ reader: typeof read; page: ClosingReportPage | null } | null>(null)
  const [selection, setSelection] = useState<{ reader: typeof read; requestId: string } | null>(null)
  const selected = selection?.reader === read ? selection?.requestId : null
  const [moreState, setMoreState] = useState<{ reader: typeof read; loading: boolean; error: string } | null>(null)
  const loadingMore = moreState?.reader === read && moreState?.loading === true
  const moreError = moreState?.reader === read ? moreState?.error : ''
  const more = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    more.current = null
    if (read) void read(query, abort.signal).then(result => {
      if (!abort.signal.aborted) setSnapshot({ reader: read, page: result.kind === 'ok' ? result.value : null })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ reader: read, page: null }) })
    return () => { abort.abort(); more.current?.abort() }
  }, [query, read])
  const current = snapshot?.reader === read ? snapshot : null
  const page = current?.page
  async function loadMore() {
    if (!read || !page?.nextCursor || loadingMore) return
    more.current?.abort()
    const abort = new AbortController()
    more.current = abort
    setMoreState({ reader: read, loading: true, error: '' })
    try {
      const result = await read({ ...query, cursor: page.nextCursor }, abort.signal)
      if (abort.signal.aborted) return
      if (result.kind !== 'ok' || result.value.branchId !== page.branchId || result.value.nextCursor === page.nextCursor || result.value.items.some(item => page.items.some(previous => previous.requestId === item.requestId))) {
        setMoreState({ reader: read, loading: false, error: 'ยังอ่านหน้าถัดไปไม่ได้ กรุณาตรวจสอบรายการใหม่' }); return
      }
      setSnapshot({ reader: read, page: { ...result.value, asOf: page.asOf, items: [...page.items, ...result.value.items] } })
    } catch { if (!abort.signal.aborted) setMoreState({ reader: read, loading: false, error: 'ยังอ่านหน้าถัดไปไม่ได้ กรุณาลองใหม่' }) }
    finally { if (!abort.signal.aborted) setMoreState(previous => previous?.reader === read ? { ...previous, loading: false } : previous) }
  }
  return <>
    {!current && read ? <p role="status">กำลังอ่านรายการรายงาน</p> : !page ? <p role={read ? 'alert' : undefined}>ยังอ่านรายการรายงานไม่ได้ กรุณาตรวจสอบรายการอีกครั้ง</p> : <>
      <p>เริ่มอ่านรายการเมื่อ <Time value={page.asOf} /> · สถานะการส่งอาจเปลี่ยนระหว่างอ่านแต่ละหน้า กดตรวจสอบใหม่เพื่ออ่านล่าสุด</p>
      {page.items.length === 0 ? <p>ไม่พบรายงานตามตัวกรองนี้</p> : <ol className="closing-report-list">{page.items.map(item => <li key={item.requestId}>
        <h4>{item.operation === 'CloseStore' ? 'ปิดร้าน' : 'ปิดกะ'} · {item.businessDate}</h4><p>กะ {item.shiftId} · รอบร้าน {item.storeSessionId}</p><p>บันทึก <Time value={item.createdAt} /></p>
        <DeliveryDetails item={item} /><LifecycleListDelivery item={item} /><button className="refresh-button" onClick={() => setSelection({ reader: read, requestId: item.requestId })} aria-pressed={selected === item.requestId}>ดูรายงานที่บันทึกไว้</button>
      </li>)}</ol>}
      {page.nextCursor && <button onClick={() => { void loadMore() }} disabled={loadingMore}>{loadingMore ? 'กำลังอ่านหน้าถัดไป' : 'อ่านรายงานเพิ่มเติม'}</button>}
      {moreError && <p role="alert">{moreError}</p>}
    </>}
    {selected && current && <Detail key={selected} requestId={selected} read={port.closingReportDetail} close={() => setSelection(null)} />}
  </>
}
function Workspace({ port, focus }: { port: Port; focus?: ClosingReportFocus | null }) {
  const [status, setStatus] = useState<ClosingReportFilter>('All')
  const [lifecycleStatus, setLifecycleStatus] = useState<ClosingLifecycleFilter>('All')
  const [store, setStore] = useState(focus?.storeSessionId ?? '')
  const [shift, setShift] = useState(focus?.shiftId ?? '')
  const [day, setDay] = useState('')
  const [query, setQuery] = useState<ClosingReportQuery>(() => ({ status: 'All', limit: 20, ...(focus ? { storeSessionId: focus.storeSessionId, shiftId: focus.shiftId } : {}) }))
  const [revision, setRevision] = useState(0)
  function apply(event: FormEvent) {
    event.preventDefault()
    setQuery({ status, ...(lifecycleStatus !== 'All' ? { lifecycleStatus } : {}), limit: 20, ...(store.trim() ? { storeSessionId: store.trim() } : {}), ...(shift.trim() ? { shiftId: shift.trim() } : {}), ...(day ? { businessDate: day } : {}) })
    setRevision(value => value + 1)
  }
  return <section className="panel closing-report-workspace" aria-label="ตรวจสอบรายงานปิดกะและปิดร้าน">
    <header className="page-header"><h3>ตรวจสอบรายงานปิดกะและปิดร้าน</h3><button className="refresh-button" onClick={() => setRevision(value => value + 1)}>ตรวจสอบรายการรายงานใหม่</button></header>
    <form className="closing-report-filters" onSubmit={apply}>
      <label>สถานะรายงานหลัก<select value={status} onChange={event => setStatus(event.target.value as ClosingReportFilter)}><option value="All">ทั้งหมด</option><option value="Pending">ยังไม่ยืนยัน</option><option value="Delivered">ส่วนกลางยืนยันแล้ว</option></select></label>
      <label>สถานะข้อมูลเปิดร้านและกะ<select value={lifecycleStatus} onChange={event => setLifecycleStatus(event.target.value as ClosingLifecycleFilter)}><option value="All">ทั้งหมด</option><option value="Pending">ยังไม่ยืนยัน</option><option value="Delivered">ส่วนกลางยืนยันแล้ว</option><option value="NotRecorded">ไม่มีข้อมูลที่บันทึกไว้</option></select></label>
      <label>วันธุรกิจของรายงาน<input type="date" value={day} onChange={event => setDay(event.target.value)} /></label>
      <label>รอบร้านที่ต้องการค้น<input value={store} onChange={event => setStore(event.target.value)} /></label><label>กะที่ต้องการค้น<input value={shift} onChange={event => setShift(event.target.value)} /></label>
      <button type="submit">ใช้ตัวกรองรายงาน</button><button type="button" onClick={() => { setStatus('All'); setLifecycleStatus('All'); setStore(''); setShift(''); setDay(''); setQuery({ status: 'All', limit: 20 }); setRevision(value => value + 1) }}>ล้างตัวกรองรายงาน</button>
    </form>
    <p>รายงานหลักและข้อมูลเปิดร้านและกะมีการยืนยันแยกกัน การยืนยันรายงานหลักไม่ได้หมายความว่าข้อมูลเปิดร้านและกะได้รับการยืนยันแล้ว</p>
    {query.lifecycleStatus === 'NotRecorded' && <p>กำลังค้นหารายงานที่ไม่มีข้อมูลเปิดร้านและกะบันทึกไว้ ไม่ถือว่าได้รับการยืนยันหรือเป็นงานค้างส่ง</p>}
    <ReportList key={JSON.stringify(query) + ':' + revision} port={port} query={query} />
  </section>
}
export function ClosingReportWorkspace({ port, scopeKey, refreshKey, focus }: { port: Port; scopeKey: string; refreshKey: number; focus?: ClosingReportFocus | null }) {
  return <Workspace key={scopeKey + ':' + refreshKey + ':' + (focus?.revision ?? 0)} port={port} focus={focus} />
}
