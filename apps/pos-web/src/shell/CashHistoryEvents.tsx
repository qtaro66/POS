import { useEffect, useRef, useState } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import { parseCashHistoryEventPage, type CashHistoryEvent, type CashHistoryEventPage, type CashHistoryEventSource, type CashHistoryProductionCounts } from '../api/cashHistoryEventContracts.ts'
import './CashHistoryEvents.css'

type Read = OrderingReadClient['cashHistoryEvents']
const dates = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
const actions: Record<string, string> = { OpenTable: 'เปิดโต๊ะ', SubmitOrder: 'ส่งออเดอร์', ChangeTable: 'ย้ายโต๊ะ', RequestPayment: 'ขอชำระเงิน', CancelPaymentRequest: 'ยกเลิกคำขอชำระ', StartPayment: 'เริ่มชำระเงิน', PaymentFailed: 'ชำระไม่สำเร็จ', CancelPayment: 'ยกเลิกการชำระที่บันทึกไว้', ConfirmPartialPayment: 'ยืนยันชำระบางส่วน', CompletePayment: 'ชำระสำเร็จ', ReverseAllConfirmedPaymentLines: 'ย้อนรายการชำระที่บันทึกไว้', CancelBill: 'ยกเลิกบิลที่บันทึกไว้', CloseTable: 'ปิดโต๊ะ' }
const statuses: Record<string, string> = { Ordering: 'รับออเดอร์', PaymentPending: 'รอชำระ', PaymentInProgress: 'กำลังชำระ', PartiallyPaid: 'ชำระบางส่วน', Completed: 'ชำระสำเร็จ', Cancelled: 'ยกเลิก', Closed: 'ปิดแล้ว' }
function Counts({ value }: { value: CashHistoryProductionCounts }) { return <span>ยังไม่เริ่ม {value.notStarted} · กำลังทำ {value.inProgress} · เสร็จแล้ว {value.completed} · ไม่ติดตามการผลิต {value.notApplicable}</span> }
function Evidence({ event, onInspectOrderLine }: { event: CashHistoryEvent; onInspectOrderLine?: (lineId: string) => void }) {
  return <li><h5>{event.session ? actions[event.session.action] : event.cancellation?.outcome === 'Confirmed' ? 'ยืนยันยกเลิกครบตามคำขอ' : 'ยืนยันยกเลิกบางส่วน'}</h5>
    <p>บันทึก <time dateTime={event.occurredAt}>{dates.format(new Date(event.occurredAt))}</time> · ผู้ดำเนินการ {event.employeeId}</p>
    <p>คำขอ {event.requestId}{event.deviceId !== null ? ` · อุปกรณ์ ${event.deviceId}` : ' · หลักฐานนี้ไม่ได้บันทึกอุปกรณ์ผู้ยืนยัน'}</p>
    {event.session && <><p>สถานะ {event.session.previousStatus === null ? 'ก่อนเปิดโต๊ะ' : statuses[event.session.previousStatus]} → {statuses[event.session.currentStatus]} · รุ่นโต๊ะ {event.session.tableVersion}</p>
      <p>รหัสโต๊ะเดิม {event.session.previousTableId} → {event.session.currentTableId}</p>
      {event.session.reason !== null && <p className="cash-history-event-reason">เหตุผลที่บันทึก: {event.session.reason}</p>}</>}
    {event.cancellation && <><ul>{event.cancellation.lines.map(line => <li key={line.orderLineId}><h6>รหัสรายการ {line.orderLineId}</h6>
      <p>{onInspectOrderLine && <button type="button" onClick={() => onInspectOrderLine(line.orderLineId)}>ดูข้อมูลตอนส่งรายการนี้</button>}</p><p>ขอยกเลิก {line.requestedQuantity} · ยังไม่ได้ยกเลิกในหลักฐานนี้ {line.notCancelledQuantity}</p>
      <p>ตรวจสถานะแล้ว: <Counts value={line.checked} /></p><p>ยืนยันยกเลิก: <Counts value={line.cancelled} /></p></li>)}</ul>
      <p>แสดงจำนวนจากหลักฐานยืนยันเดิม ไม่ใช่มูลค่ายกเลิกหรือการคืนเงิน และไม่เติมชื่อสินค้าจากข้อมูลปัจจุบัน</p></>}
  </li>
}
function Source({ read, requestId, tableSessionId, branchId, source, onInspectOrderLine }: { onInspectOrderLine?: (lineId: string) => void; read: Read; requestId: string; tableSessionId: string; branchId: string; source: CashHistoryEventSource }) {
  const [snapshot, setSnapshot] = useState<{ owner: Read; page: CashHistoryEventPage | null } | null>(null)
  const [moreState, setMoreState] = useState<{ owner: Read; loading: boolean; error: string } | null>(null)
  const moreLoading = moreState !== null && moreState.owner === read && moreState.loading
  const moreError = moreState !== null && moreState.owner === read ? moreState.error : ''
  const continuation = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, { source, limit: 20 }, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let page: CashHistoryEventPage | null = null
      try { if (result.kind === 'ok') page = parseCashHistoryEventPage(result.value, branchId, requestId, { source, limit: 20 }, tableSessionId) } catch { /* Invalid evidence remains unavailable. */ }
      setSnapshot({ owner: read, page })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ owner: read, page: null }) })
    return () => { abort.abort(); continuation.current?.abort() }
  }, [read, requestId, tableSessionId, branchId, source])
  const current = snapshot?.owner === read ? snapshot : null, page = current?.page
  async function more() {
    if (!read || !page?.nextCursor || moreLoading) return
    continuation.current?.abort()
    const abort = new AbortController(); continuation.current = abort; setMoreState({ owner: read, loading: true, error: '' })
    try {
      const result = await read(requestId, { source, limit: 20, cursor: page.nextCursor }, abort.signal)
      if (abort.signal.aborted) return
      if (result.kind !== 'ok') throw new Error('Evidence unavailable')
      const next = parseCashHistoryEventPage(result.value, branchId, requestId, { source, limit: 20, cursor: page.nextCursor }, tableSessionId)
      if (next.nextCursor === page.nextCursor || next.events.some(event => page.events.some(previous => previous.eventId === event.eventId))) throw new Error('Invalid continuation')
      setSnapshot({ owner: read, page: { ...next, events: [...page.events, ...next.events] } })
    } catch { if (!abort.signal.aborted) setMoreState({ owner: read, loading: false, error: 'ยังอ่านหลักฐานหน้าถัดไปไม่ได้ กรุณาอ่านหลักฐานใหม่' }) }
    finally { if (!abort.signal.aborted) setMoreState(previous => previous?.owner === read ? { ...previous, loading: false } : previous) }
  }
  return !read ? <p>ยังไม่เชื่อมต่อหลักฐานเหตุการณ์จากบริการสาขารุ่นนี้</p> : !current ? <p role="status">กำลังอ่านหลักฐานเหตุการณ์</p> : !page ? <p role="alert">ยังอ่านหลักฐานเหตุการณ์ไม่ได้ ยอดชำระที่บันทึกยังคงเดิม</p> : <>
    <p>อ่านหลักฐานเมื่อ <time dateTime={page.asOf}>{dates.format(new Date(page.asOf))}</time> · เรียงตามลำดับบันทึกล่าสุดของแหล่งนี้</p>
    {page.events.length === 0 ? <p>ไม่พบหลักฐานจากแหล่งนี้ในฐานข้อมูลสาขาที่อ่านได้</p> : <ol className="cash-history-events-list">{page.events.map(event => <Evidence key={event.eventId} event={event} onInspectOrderLine={onInspectOrderLine} />)}</ol>}
    {page.nextCursor && <button type="button" disabled={moreLoading} onClick={() => { void more() }}>{moreLoading ? 'กำลังอ่านหลักฐานเพิ่มเติม' : 'อ่านหลักฐานเพิ่มเติม'}</button>}
    {moreError && <p role="alert">{moreError}</p>}
  </>
}
export function CashHistoryEvents({ read, branchId, requestId, tableSessionId, onInspectOrderLine }: { onInspectOrderLine?: (lineId: string) => void; read: Read; branchId: string; requestId: string; tableSessionId: string }) {
  const [source, setSource] = useState<CashHistoryEventSource>('Session'), [revision, setRevision] = useState(0)
  return <section aria-label="หลักฐานเหตุการณ์ที่เกี่ยวข้อง" className="cash-history-events"><h4>หลักฐานเหตุการณ์ที่เกี่ยวข้อง</h4>
    <div className="cash-history-event-controls"><button type="button" aria-pressed={source === 'Session'} onClick={() => setSource('Session')}>สถานะและย้ายโต๊ะ</button>
      <button type="button" aria-pressed={source === 'Cancellation'} onClick={() => setSource('Cancellation')}>หลักฐานยืนยันการยกเลิก</button>
      <button type="button" onClick={() => setRevision(value => value + 1)}>อ่านหลักฐานใหม่</button></div>
    <p>อ่านแยกตามแหล่งหลักฐาน ไม่ใช่ลำดับเวลารวมทั้งหมด การอ่านไม่เปลี่ยนยอดเงินหรือสร้างคำสั่งใหม่</p>
    <Source key={JSON.stringify([branchId, requestId, tableSessionId, source, revision])} read={read} branchId={branchId} requestId={requestId} tableSessionId={tableSessionId} source={source} onInspectOrderLine={onInspectOrderLine} />
  </section>
}
