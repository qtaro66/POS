import { CashHistoryProductionOrders } from './CashHistoryProductionOrders.tsx'
import { CashHistoryEvents } from './CashHistoryEvents.tsx'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import type { CashHistoryBill, CashHistoryDetail, CashHistoryDiscount, CashHistoryItem, CashHistoryPage, CashHistoryQuery } from '../api/cashHistoryContracts.ts'
import './CashHistoryWorkspace.css'

type Port = Pick<OrderingReadClient, 'cashHistory' | 'cashHistoryDetail' | 'cashHistoryEvents' | 'cashHistoryProductionOrders' | 'cashHistoryProductionOrderDetail'>
const money = (value: number) => value.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 20 })
const date = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
function Time({ value }: { value: string }) { return <time dateTime={value}>{date.format(new Date(value))}</time> }
const same = (left: CashHistoryItem, right: CashHistoryItem) => Object.keys(left).every(key => {
  if (key === 'orderBatchIds' && Array.isArray(left.orderBatchIds)) return Array.isArray(right.orderBatchIds) && left.orderBatchIds.length === right.orderBatchIds.length && left.orderBatchIds.every(id => right.orderBatchIds!.includes(id))
  return left[key as keyof CashHistoryItem] === right[key as keyof CashHistoryItem]
})
function Discount({ value }: { value: CashHistoryDiscount | null }) {
  return value ? <span>{value.kind === 'Percentage' ? `${value.requestedValue}%` : `${money(value.requestedValue)} บาท`} · ส่วนลดที่บันทึก {money(value.appliedAmount)} บาท</span> : <span>ไม่มีส่วนลดที่บันทึกไว้</span>
}
function Bill({ value }: { value: CashHistoryBill }) {
  return <section aria-label="รายการสินค้าจากใบคิดเงินเดิม" className="cash-history-bill">
    <h4>รายการสินค้าที่บันทึกไว้</h4>
    <p>รหัสโต๊ะ ณ คิดเงิน {value.tableId} · คำขอคิดเงินเดิม {value.paymentRequestId}</p>
    <p>คิดเงินเมื่อ <Time value={value.capturedAt} /> · ข้อมูลเมนูรุ่น {value.masterDataVersion} · นโยบายคำนวณรุ่น {value.calculationPolicyVersion}</p>
    <p>VAT ที่บันทึก {value.vatRate}% · ค่าบริการที่บันทึก {value.serviceChargeRate}%</p>
    {value.items.length === 0 ? <p>ใบคิดเงินเดิมบันทึกรายการสินค้าเป็นรายการว่าง</p> : <ol>{value.items.map(line => <li key={line.orderLineId}>
      <h5 className="cash-history-recorded-text">{line.displayName}</h5><p>ชุดออเดอร์ {line.orderBatchId} · รหัสรายการ {line.orderLineId}</p>
      <p>{line.fulfillmentType === 'DineIn' ? 'ทานที่ร้าน' : 'กลับบ้าน'} · จำนวนที่คิดเงิน {line.quantity} · ราคาต่อหน่วยที่บันทึก {money(line.unitPrice)} บาท</p>
      {line.options.length > 0 && <ul aria-label={'ตัวเลือกของ ' + line.orderLineId}>{line.options.map((option, index) => <li key={index} className="cash-history-recorded-text">{option.displayName} ×{option.quantityPerItem} ต่อหน่วย · {money(option.unitPrice)} บาท</li>)}</ul>}
      {line.comment !== null && <p className="cash-history-recorded-text">หมายเหตุ: {line.comment}</p>}
      <p>ยอดก่อนส่วนลดที่บันทึก {money(line.grossAmount)} บาท · <Discount value={line.discount} /> · ยอดหลังส่วนลดที่บันทึก {money(line.netAmount)} บาท</p>
    </li>)}</ol>}
    <p>ส่วนลดท้ายบิลเดิม: <Discount value={value.billDiscount} /></p>
    <p>ราคาและยอดเป็นข้อมูลที่บันทึกเดิม ไม่คำนวณใหม่ด้วยเมนูหรือนโยบายปัจจุบัน</p>
  </section>
}
function Detail({ item, read, eventsRead, productionRead, productionDetailRead }: { productionRead: Port['cashHistoryProductionOrders']; productionDetailRead: Port['cashHistoryProductionOrderDetail']; item: CashHistoryItem; read: Port['cashHistoryDetail']; eventsRead: Port['cashHistoryEvents'] }) {
  const [focus, setFocus] = useState<{ orderLineId: string; revision: number } | undefined>(undefined)
  const [snapshot, setSnapshot] = useState<{ owner: typeof read; value: CashHistoryDetail | null } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(item.requestId, abort.signal).then(result => {
      if (!abort.signal.aborted) setSnapshot({ owner: read, value: result.kind === 'ok' && same(item, result.value.transaction) ? result.value : null })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ owner: read, value: null }) })
    return () => abort.abort()
  }, [item, read])
  const current = snapshot?.owner === read ? snapshot : null
  const detail = current?.value, value = detail?.transaction
  return <section aria-label="รายละเอียดธุรกรรมเงินสด" className="cash-history-detail">
    <h3>รายละเอียดที่บันทึกไว้</h3>
    {!current ? <p role="status">กำลังอ่านรายละเอียดธุรกรรม</p> : !value || !detail ? <p role="alert">ยังอ่านรายละเอียดนี้ไม่ได้ กรุณาค้นหาใหม่</p> : <>
      <h4>{value.receiptNumber}</h4><p>ชำระเงินสดสำเร็จ · วันธุรกิจ {value.businessDate}</p>
      <dl className="store-attribution"><div><dt>บันทึกเมื่อ</dt><dd><Time value={value.capturedAt} /></dd></div>
        <div><dt>รับเงินสด</dt><dd>{money(value.tenderedAmount)} บาท</dd></div><div><dt>ยอดชำระที่มีผล</dt><dd>{money(value.appliedAmount)} บาท</dd></div>
        <div><dt>เงินทอน</dt><dd>{money(value.changeAmount)} บาท</dd></div><div><dt>พนักงาน</dt><dd>{value.employeeId}</dd></div>
        <div><dt>อุปกรณ์ที่บันทึก</dt><dd>{value.deviceId}</dd></div><div><dt>รหัสรอบโต๊ะ</dt><dd>{value.tableSessionId}</dd></div>
        <div><dt>รอบร้าน / กะ</dt><dd>{value.storeSessionId} / {value.shiftId}</dd></div></dl>
      <p>สถานะใบเสร็จ ณ บันทึก: รอจำลองใบเสร็จ ไม่ใช่หลักฐานว่าสั่งพิมพ์แล้ว</p>
      {detail.financials ? <section aria-label="ยอดจาก snapshot เดิม"><h4>ยอดจากข้อมูลเดิม</h4><dl className="store-attribution">{([
        ['ยอดสินค้าก่อนส่วนลด', 'itemGrossAmount'], ['ส่วนลดสินค้า', 'lineDiscountAmount'], ['ส่วนลดท้ายบิล', 'billDiscountAmount'],
        ['ค่าบริการ', 'serviceChargeAmount'], ['ยอดก่อนปัดเศษ', 'totalBeforeRounding'], ['ปัดเศษ', 'roundingAdjustment'],
        ['ยอดสุทธิ', 'amountDue'], ['VAT รวมในราคา', 'vatInclusiveAmount'], ['ยอดก่อน VAT', 'amountBeforeVat'],
      ] as const).map(([label, key]) => <div key={key}><dt>{label}</dt><dd>{money(detail.financials![key])} บาท</dd></div>)}</dl></section> : <p>รายการเดิมไม่มีรายละเอียดส่วนลด ภาษี และยอดแยกที่บันทึกไว้</p>}
      {detail.bill ? <Bill value={detail.bill} /> : <p>{detail.bill === null ? 'รายการเดิมไม่ได้บันทึกรายละเอียดสินค้าที่อ่านได้' : 'ยังไม่ทราบรายละเอียดสินค้าเดิมจากบริการสาขารุ่นนี้'} ไม่คำนวณย้อนหลังจากข้อมูลปัจจุบัน</p>}
      <CashHistoryEvents onInspectOrderLine={productionRead && productionDetailRead ? orderLineId => setFocus(old => ({ orderLineId, revision: (old?.revision ?? 0) + 1 })) : undefined} read={eventsRead} branchId={detail.branchId} requestId={value.requestId} tableSessionId={value.tableSessionId} />
      <CashHistoryProductionOrders read={productionRead} detailRead={productionDetailRead} branchId={detail.branchId} requestId={value.requestId} tableSessionId={value.tableSessionId} focus={focus} />
      <p>หลักฐานนี้ไม่ครอบคลุมเหตุการณ์ปรับราคาหรือมูลค่าการยกเลิกที่ไม่ได้บันทึกไว้</p>
    </>}
  </section>
}
function Results({ port, query }: { port: Port; query: CashHistoryQuery }) {
  const read = port.cashHistory
  const [snapshot, setSnapshot] = useState<{ owner: typeof read; page: CashHistoryPage | null } | null>(null)
  const [selected, setSelected] = useState<CashHistoryItem | null>(null)
  const [moreState, setMoreState] = useState({ loading: false, error: '' })
  const continuation = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(query, abort.signal).then(result => {
      if (!abort.signal.aborted) setSnapshot({ owner: read, page: result.kind === 'ok' ? result.value : null })
    }).catch(() => { if (!abort.signal.aborted) setSnapshot({ owner: read, page: null }) })
    return () => { abort.abort(); continuation.current?.abort() }
  }, [query, read])
  const current = snapshot?.owner === read ? snapshot : null, page = current?.page
  async function more() {
    if (!read || !page?.nextCursor || moreState.loading) return
    const abort = new AbortController(); continuation.current = abort; setMoreState({ loading: true, error: '' })
    try {
      const result = await read({ ...query, cursor: page.nextCursor }, abort.signal)
      if (abort.signal.aborted) return
      if (result.kind !== 'ok' || result.value.branchId !== page.branchId || result.value.businessDate !== page.businessDate || result.value.nextCursor === page.nextCursor ||
        result.value.items.some(item => page.items.some(previous => previous.requestId === item.requestId))) {
        setMoreState({ loading: false, error: 'ยังอ่านหน้าถัดไปไม่ได้ กรุณาค้นหาใหม่' }); return
      }
      setSnapshot({ owner: read, page: { ...result.value, asOf: page.asOf, items: [...page.items, ...result.value.items] } })
    } catch { if (!abort.signal.aborted) setMoreState({ loading: false, error: 'ยังอ่านหน้าถัดไปไม่ได้ กรุณาค้นหาใหม่' }) }
    finally { if (!abort.signal.aborted) setMoreState(previous => ({ ...previous, loading: false })) }
  }
  return <div className="cash-history-split"><section aria-label="รายการธุรกรรมเงินสด">
    <h3>รายการล่าสุดก่อน</h3>
    {!read ? <p>ยังไม่เชื่อมต่อประวัติธุรกรรม</p> : !current ? <p role="status">กำลังอ่านประวัติธุรกรรม</p> : !page ? <p role="alert">ยังอ่านประวัติธุรกรรมไม่ได้ กรุณาค้นหาใหม่</p> : <>
      <p>วันธุรกิจ {page.businessDate} · อ่านเมื่อ <Time value={page.asOf} /></p>
      {page.items.length === 0 ? <p>ไม่พบธุรกรรมตามตัวกรองนี้</p> : <ol className="cash-history-list">{page.items.map(item => <li key={item.requestId}>
        <button type="button" aria-pressed={selected?.requestId === item.requestId} onClick={() => setSelected(item)}>
          <strong>{item.receiptNumber}</strong><span>เงินสด · ชำระสำเร็จ · {money(item.appliedAmount)} บาท</span>
          <span>{item.tableId ? `รหัสโต๊ะเดิม ${item.tableId}` : item.tableId === null ? 'ไม่มีรหัสโต๊ะเดิมที่บันทึกไว้' : 'ยังไม่ทราบรหัสโต๊ะเดิม'}</span>
          {item.orderBatchIds && <span>ชุดออเดอร์เดิม {item.orderBatchIds.join(', ') || 'ไม่มีรายการชุดออเดอร์'}</span>}
          <Time value={item.capturedAt} /></button>
      </li>)}</ol>}
      {page.nextCursor && <button type="button" onClick={() => { void more() }} disabled={moreState.loading}>{moreState.loading ? 'กำลังอ่านหน้าถัดไป' : 'อ่านธุรกรรมเพิ่มเติม'}</button>}
      {moreState.error && <p role="alert">{moreState.error}</p>}
    </>}
  </section>{selected && page ? <Detail key={selected.requestId} item={selected} read={port.cashHistoryDetail} eventsRead={port.cashHistoryEvents} productionRead={port.cashHistoryProductionOrders} productionDetailRead={port.cashHistoryProductionOrderDetail} /> : <p className="cash-history-detail">เลือกรายการเพื่อดูรายละเอียดที่บันทึกไว้</p>}</div>
}
function Workspace({ port }: { port: Port }) {
  const [day, setDay] = useState(''), [receipt, setReceipt] = useState(''), [table, setTable] = useState('')
  const [recordedTable, setRecordedTable] = useState(''), [batch, setBatch] = useState('')
  const [method, setMethod] = useState<'All' | 'Cash'>('All'), [status, setStatus] = useState<'All' | 'Completed'>('All')
  const [query, setQuery] = useState<CashHistoryQuery>({ limit: 20 }), [revision, setRevision] = useState(0)
  function apply(event: FormEvent) {
    event.preventDefault(); setQuery({ limit: 20, paymentMethod: method, status,
      ...(day ? { businessDate: day } : {}), ...(receipt.trim() ? { receiptNumber: receipt.trim() } : {}), ...(table.trim() ? { tableSessionId: table.trim() } : {}),
      ...(recordedTable.trim() ? { tableId: recordedTable.trim() } : {}), ...(batch.trim() ? { orderBatchId: batch.trim() } : {}) }); setRevision(value => value + 1)
  }
  return <section className="panel cash-history-workspace" aria-label="ประวัติรายการชำระเงินสด">
    <header className="page-header"><h2>ประวัติธุรกรรม</h2><button type="button" onClick={() => setRevision(value => value + 1)}>อ่านประวัติใหม่</button></header>
    <p>อ่านอย่างเดียว · มุมมองนี้แสดงรายการชำระเงินสดที่บันทึกไว้ในสาขานี้ ใบเสร็จเป็นข้อมูลจากขั้นตอนจำลองในระบบปัจจุบัน</p>
    <p>ค่าเริ่มต้นใช้วันธุรกิจปัจจุบันจากสาขา ช่องทางและรายละเอียดประเภทธุรกรรมอื่นยังไม่รองรับ</p>
    <form className="cash-history-filters" onSubmit={apply}>
      <label>วันธุรกิจ<input type="date" value={day} onChange={event => setDay(event.target.value)} /></label>
      <label>เลขใบเสร็จ<input value={receipt} maxLength={128} onChange={event => setReceipt(event.target.value)} /></label>
      <label>รหัสรอบโต๊ะ<input value={table} onChange={event => setTable(event.target.value)} /></label>
      <label>รหัสโต๊ะ ณ คิดเงิน<input value={recordedTable} maxLength={256} onChange={event => setRecordedTable(event.target.value)} /></label>
      <label>รหัสชุดออเดอร์เดิม<input value={batch} maxLength={256} onChange={event => setBatch(event.target.value)} /></label>
      <label>ช่องทางชำระ<select value={method} onChange={event => setMethod(event.target.value as 'All' | 'Cash')}><option value="All">ทั้งหมดที่รองรับ</option><option value="Cash">เงินสด</option></select></label>
      <label>สถานะธุรกรรม<select value={status} onChange={event => setStatus(event.target.value as 'All' | 'Completed')}><option value="All">ทั้งหมดที่รองรับ</option><option value="Completed">ชำระสำเร็จ</option></select></label>
      <button type="submit">ค้นหาธุรกรรม</button><button type="button" onClick={() => { setDay(''); setReceipt(''); setTable(''); setRecordedTable(''); setBatch(''); setMethod('All'); setStatus('All'); setQuery({ limit: 20 }); setRevision(value => value + 1) }}>ล้างตัวกรองธุรกรรม</button>
    </form>
    <Results key={JSON.stringify(query) + ':' + revision} port={port} query={query} />
  </section>
}
export function CashHistoryWorkspace({ port, scopeKey }: { port: Port; scopeKey: string }) { return <Workspace key={scopeKey} port={port} /> }
