import { CashHistoryPrintEvidence } from './CashHistoryPrintEvidence.tsx'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import { parseCashHistoryProductionPage, parseCashHistoryProductionDetail, type CashHistoryProductionQuery, type CashHistoryProductionPage, type CashHistoryProductionItem, type CashHistoryProductionDetail } from '../api/cashHistoryProductionContracts.ts'
const dates = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
type Read = OrderingReadClient['cashHistoryProductionOrders']
type DetailRead = OrderingReadClient['cashHistoryProductionOrderDetail']
interface Props { printRead?: OrderingReadClient['cashHistoryPrintEvidence']; attemptsRead?: OrderingReadClient['cashHistoryPrintAttempts']; read: Read; detailRead: DetailRead; branchId: string; requestId: string; tableSessionId: string; focus?: { orderLineId: string; revision: number } }
function Detail({ read, item, branchId, requestId, tableSessionId, printRead, attemptsRead }: { printRead?: Props['printRead']; attemptsRead?: Props['attemptsRead']; read: DetailRead; item: CashHistoryProductionItem; branchId: string; requestId: string; tableSessionId: string }) {
  const [state, setState] = useState<{ owner: DetailRead; detail: CashHistoryProductionDetail | null } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, item.snapshotId, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let detail: CashHistoryProductionDetail | null = null
      try { if (result.kind === 'ok') { const parsed = parseCashHistoryProductionDetail(result.value, branchId, requestId, item.snapshotId, tableSessionId); if (JSON.stringify(parsed.snapshot) === JSON.stringify(item)) detail = parsed } } catch { /* Invalid source remains unavailable. */ }
      setState({ owner: read, detail })
    }).catch(() => { if (!abort.signal.aborted) setState({ owner: read, detail: null }) })
    return () => abort.abort()
  }, [read, item, branchId, requestId, tableSessionId])
  const current = state?.owner === read ? state : null
  return <section aria-label="รายละเอียดงานผลิตเดิม"><h5>ข้อมูลรายการตอนส่ง</h5>{!current ? <p role="status">กำลังอ่านข้อมูลตอนส่ง</p> : !current.detail ? <p role="alert">ยังอ่านข้อมูลตอนส่งนี้ไม่ได้ ยอดชำระที่บันทึกยังคงเดิม</p> : <>
    <p>รหัสโต๊ะตอนส่ง {item.tableId} · สถานีเดิม {item.productionStationId} · ชุดออเดอร์ {item.orderBatchId}</p><p>คำขอส่งเดิม {item.submittedRequestId} · บันทึกเมื่อ <time dateTime={item.submittedAt}>{dates.format(new Date(item.submittedAt))}</time></p>
    <ol>{current.detail.lines.map(line => <li key={line.orderLineId}><h6 className="cash-history-recorded-text">{line.displayName}</h6><p>รหัสรายการ {line.orderLineId} · ลำดับเดิม {line.lineSequence}</p><p>จำนวนตอนส่ง {line.quantity} · {line.fulfillmentType === 'DineIn' ? 'ทานที่ร้าน' : 'กลับบ้าน'}</p>
      {line.options.length > 0 && <ul>{line.options.map((o, index) => <li key={index} className="cash-history-recorded-text">{o.displayName} ×{o.quantityPerItem} ต่อหน่วย</li>)}</ul>}{line.comment !== null && <p className="cash-history-recorded-text">หมายเหตุเดิม: {line.comment}</p>}</li>)}</ol>
    <CashHistoryPrintEvidence read={printRead} attemptsRead={attemptsRead} branchId={branchId} requestId={requestId} snapshot={item} />
  </>}</section>
}
function Results({ read, detailRead, branchId, requestId, tableSessionId, query, printRead, attemptsRead }: Props & { query: CashHistoryProductionQuery }) {
  const [state, setState] = useState<{ owner: Read; page: CashHistoryProductionPage | null } | null>(null)
  const [selected, setSelected] = useState<{ owner: Read; item: CashHistoryProductionItem } | null>(null)
  const [moreState, setMoreState] = useState<{ owner: Read; loading: boolean; error: string } | null>(null)
  const continuation = useRef<AbortController | null>(null)
  const moreLoading = moreState !== null && moreState.owner === read && moreState.loading
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, query, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let page: CashHistoryProductionPage | null = null
      try { if (result.kind === 'ok') page = parseCashHistoryProductionPage(result.value, branchId, requestId, query, tableSessionId) } catch { /* Invalid source remains unavailable. */ }
      setState({ owner: read, page })
    }).catch(() => { if (!abort.signal.aborted) setState({ owner: read, page: null }) })
    return () => { abort.abort(); continuation.current?.abort() }
  }, [read, requestId, query, branchId, tableSessionId])
  const current = state?.owner === read ? state : null, page = current?.page
  async function more() {
    if (!read || !page?.nextCursor || moreLoading) return
    const abort = new AbortController(); continuation.current = abort; setMoreState({ owner: read, loading: true, error: '' })
    try {
      const result = await read(requestId, { ...query, cursor: page.nextCursor }, abort.signal)
      if (abort.signal.aborted) return
      if (result.kind !== 'ok') throw new Error('Unavailable')
      const next = parseCashHistoryProductionPage(result.value, branchId, requestId, query, tableSessionId)
      if (next.nextCursor === page.nextCursor || next.items.some(i => page.items.some(old => old.snapshotId === i.snapshotId))) throw new Error('Invalid continuation')
      setState({ owner: read, page: { ...next, items: [...page.items, ...next.items] } })
    } catch { if (!abort.signal.aborted) setMoreState({ owner: read, loading: false, error: 'ยังอ่านงานผลิตเดิมเพิ่มเติมไม่ได้ กรุณาอ่านใหม่' }) }
    finally { if (!abort.signal.aborted) setMoreState(old => old?.owner === read ? { ...old, loading: false } : old) }
  }
  return !current ? <p role="status">กำลังอ่านงานผลิตเดิม</p> : !page ? <p role="alert">ยังอ่านงานผลิตเดิมไม่ได้ ยอดชำระที่บันทึกยังคงเดิม</p> : <>
    {page.items.length === 0 ? <p>ไม่พบข้อมูลงานผลิตเดิมตามตัวกรองในข้อมูลสาขาที่อ่านได้ ไม่ได้ยืนยันว่าไม่มีออเดอร์</p> : <ul>{page.items.map(item => <li key={item.snapshotId}><button type="button" aria-pressed={selected?.owner === read && selected?.item.snapshotId === item.snapshotId} onClick={() => setSelected({ owner: read, item })}>ดูงานผลิตเดิม {item.productionStationId} · {item.orderBatchId} · {item.lineCount} รายการ</button></li>)}</ul>}
    {page.nextCursor && <button type="button" disabled={moreLoading} onClick={() => { void more() }}>{moreLoading ? 'กำลังอ่านงานผลิตเดิมเพิ่มเติม' : 'อ่านงานผลิตเดิมเพิ่มเติม'}</button>}{moreState !== null && moreState.owner === read && moreState.error && <p role="alert">{moreState.error}</p>}
    {selected && selected.owner === read && page.items.some(item => item.snapshotId === selected.item.snapshotId) && <Detail key={selected.item.snapshotId} printRead={printRead} attemptsRead={attemptsRead} read={detailRead} item={selected.item} branchId={branchId} requestId={requestId} tableSessionId={tableSessionId} />}
  </>
}
function Panel(props: Props) {
  const [batch, setBatch] = useState(''), [line, setLine] = useState(props.focus?.orderLineId ?? ''), [station, setStation] = useState('')
  const [query, setQuery] = useState<CashHistoryProductionQuery>({ limit: 20, ...(props.focus ? { orderLineId: props.focus.orderLineId } : {}) }), [revision, setRevision] = useState(0)
  function apply(event: FormEvent) { event.preventDefault(); setQuery({ limit: 20, ...(batch ? { orderBatchId: batch } : {}), ...(line ? { orderLineId: line } : {}), ...(station ? { productionStationId: station } : {}) }); setRevision(v => v + 1) }
  return <><p>อ่านข้อมูลตอนส่งเฉพาะรายการที่มีสถานีผลิต จำนวนตอนส่งอาจต่างจากจำนวนที่คิดเงินหลังยกเลิก หลักฐานนี้ไม่ยืนยันว่าพิมพ์หรือผลิตสำเร็จ</p>
    {!props.read || !props.detailRead ? <p>ยังไม่เชื่อมต่อข้อมูลงานผลิตเดิมจากบริการสาขารุ่นนี้</p> : <><form className="cash-history-filters" onSubmit={apply}><label>ชุดออเดอร์ตอนส่ง<input maxLength={256} value={batch} onChange={e => setBatch(e.target.value)} /></label><label>รหัสรายการตอนส่ง<input maxLength={256} value={line} onChange={e => setLine(e.target.value)} /></label><label>สถานีผลิตเดิม<input maxLength={256} value={station} onChange={e => setStation(e.target.value)} /></label>
      <button type="submit">ค้นหางานผลิตเดิม</button><button type="button" onClick={() => { setBatch(''); setLine(''); setStation(''); setQuery({ limit: 20 }); setRevision(v => v + 1) }}>ล้างตัวกรองงานผลิต</button><button type="button" onClick={() => setRevision(v => v + 1)}>อ่านงานผลิตเดิมใหม่</button></form><Results key={JSON.stringify([query, revision])} {...props} query={query} /></>}
  </>
}
export function CashHistoryProductionOrders(props: Props) {
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { if (props.focus) heading.current?.focus() }, [props.focus])
  return <section aria-label="งานผลิตที่บันทึกตอนส่ง"><h4 tabIndex={-1} ref={heading}>งานผลิตที่บันทึกตอนส่ง</h4><Panel key={JSON.stringify([props.branchId, props.requestId, props.tableSessionId, props.focus])} {...props} /></section>
}
