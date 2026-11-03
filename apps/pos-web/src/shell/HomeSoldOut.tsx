import { useEffect, useRef, useState } from 'react'
import type { SoldOutAdjustment, SoldOutAdjustmentOutcome, SoldOutItem, SoldOutPage } from '../api/soldOutContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'
import { FreshnessValue } from './DataFreshnessPanel.tsx'

type Read = NonNullable<OrderingReadClient['soldOut']>
type Adjust = OrderingReadClient['adjustSoldOut']

function SoldOutResults({ read, search, offset, expanded, onNext, onClear }: {
  read: Read; search: string; offset: number; expanded: boolean; onNext: (offset: number) => void; onClear?: (item: SoldOutItem) => void
}) {
  const [observation, setObservation] = useState<{ source: Read; page: SoldOutPage | null }>()
  const page = observation?.source === read ? observation.page : undefined
  useEffect(() => {
    const controller = new AbortController()
    void (async () => {
      try {
        const result = await read(search, offset, controller.signal)
        if (!controller.signal.aborted) setObservation({ source: read, page: result.kind === 'ok' ? result.value : null })
      } catch {
        if (!controller.signal.aborted) setObservation({ source: read, page: null })
      }
    })()
    return () => controller.abort()
  }, [read, search, offset])
  if (page === undefined) return <p role="status">กำลังอ่านรายการ Sold Out</p>
  if (page === null) return <p role="alert">อ่านรายการ Sold Out ไม่ได้</p>
  return <>
    <p role="status">{search ? 'พบ' : 'ทั้งหมด'} {page.totalCount} รายการ</p>
    {page.items.length === 0 && <p>ไม่มีรายการในหน้านี้</p>}
    <ul>{(expanded ? page.items : page.items.slice(0, 6)).map(item => <li key={item.sellableItemId}>
      <strong>{item.displayName ?? item.sellableItemId}</strong>
      {item.displayName && <> · {item.sellableItemId}</>}
      {item.stationId && <> · {item.stationId}</>}
      {' · '}{item.changedByEmployeeId}{' · '}
      <FreshnessValue reading={{ phase: 'ready', timestamp: item.changedAt }} />
      {expanded && onClear && <button className="refresh-button" onClick={() => onClear(item)}
        aria-label={`ยกเลิก Sold Out ${item.displayName ?? item.sellableItemId}`}>ยกเลิก Sold Out</button>}
    </li>)}</ul>
    {expanded && page.nextOffset !== null &&
      <button className="refresh-button" onClick={() => onNext(page.nextOffset!)}>Sold Out หน้าถัดไป</button>}
  </>
}

const outcomes: Record<SoldOutAdjustmentOutcome, string> = {
  Applied: 'บันทึก Sold Out แล้ว',
  Replayed: 'ระบบเคยบันทึกคำขอนี้แล้ว',
  Conflict: 'สถานะเปลี่ยนแล้ว กรุณาตรวจสอบรายการล่าสุด',
  ItemNotFound: 'ไม่พบสินค้าในแค็ตตาล็อกปัจจุบัน',
  Rejected: 'คำขอไม่ผ่านหรืออุปกรณ์ไม่มีสิทธิ์',
  Unknown: 'ยังยืนยันผลไม่ได้ ตรวจสอบรายการล่าสุดหรือลองคำขอเดิมอีกครั้ง',
}

export function HomeSoldOut({ read, adjust }: { read?: Read; adjust?: Adjust }) {
  const [expanded, setExpanded] = useState(false)
  const [draft, setDraft] = useState('')
  const [query, setQuery] = useState({ search: '', offset: 0, revision: 0 })
  const [itemId, setItemId] = useState('')
  const [confirmation, setConfirmation] = useState<SoldOutItem>()
  const [pending, setPending] = useState<{ source: Adjust; request: SoldOutAdjustment }>()
  const [result, setResult] = useState<{ source: Adjust; outcome: SoldOutAdjustmentOutcome }>()
  const active = useRef<AbortController | undefined>(undefined)
  const dialog = useRef<HTMLDialogElement>(null)
  const currentPending = pending?.source === adjust ? pending : undefined
  const currentResult = result?.source === adjust ? result : undefined
  useEffect(() => {
    const modal = dialog.current
    return () => {
      active.current?.abort()
      active.current = undefined
      if (modal?.open) modal.close()
    }
  }, [adjust])

  function reload(search = '', offset = 0) {
    setQuery(previous => ({ search, offset, revision: previous.revision + 1 }))
  }
  async function send(request: SoldOutAdjustment) {
    if (!adjust || active.current) return
    const controller = new AbortController()
    active.current = controller
    setPending({ source: adjust, request })
    setResult(undefined)
    let outcome: SoldOutAdjustmentOutcome = 'Unknown'
    try { outcome = await adjust(request, controller.signal) } catch { /* Delivery may already have committed. */ }
    if (controller.signal.aborted) return
    active.current = undefined
    setResult({ source: adjust, outcome })
    if (outcome !== 'Unknown') {
      setPending(undefined)
      if (outcome === 'Applied' || outcome === 'Replayed') setItemId('')
    }
    reload(query.search)
  }
  function start(id: string, soldOut: boolean) {
    void send({ operationId: crypto.randomUUID(), sellableItemId: id, expectedSoldOut: !soldOut, soldOut })
  }
  return <section aria-labelledby="home-sold-out-title">
    <h3 id="home-sold-out-title">รายการ Sold Out ของสาขา</h3>
    {!read ? <p>ยังไม่เชื่อมต่อรายการ Sold Out</p> : <>
      <button className="refresh-button" aria-expanded={expanded} onClick={() => {
        setExpanded(!expanded)
        setDraft('')
        reload()
      }}>{expanded ? 'กลับไปดูรายการล่าสุด' : 'ดูรายการ Sold Out ทั้งหมด'}</button>
      {expanded && <form onSubmit={event => { event.preventDefault(); reload(draft.trim()) }}>
        <label>ชื่อเมนูหรือรหัสสินค้า <input value={draft} maxLength={100} onChange={event => setDraft(event.target.value)} /></label>
        <button className="refresh-button" type="submit">ค้นหา Sold Out</button>
      </form>}
      {expanded && adjust && <form onSubmit={event => { event.preventDefault(); if (itemId.trim() && !currentPending) start(itemId.trim(), true) }}>
        <label>รหัสสินค้าที่จะตั้ง Sold Out <input value={itemId} maxLength={128} onChange={event => setItemId(event.target.value)} /></label>
        <button className="refresh-button" type="submit" disabled={!itemId.trim() || !!currentPending}>ตั้ง Sold Out</button>
      </form>}
      {currentPending && !currentResult && <p role="status">กำลังบันทึก Sold Out</p>}
      {currentResult && <p role="status">{outcomes[currentResult.outcome]}</p>}
      {currentPending && currentResult?.outcome === 'Unknown' &&
        <button className="refresh-button" onClick={() => { void send(currentPending.request) }}>ลองคำขอเดิมอีกครั้ง</button>}
      <button className="refresh-button" onClick={() => reload(query.search)}>อ่านรายการ Sold Out ใหม่</button>
      {expanded && query.offset > 0 &&
        <button className="refresh-button" onClick={() => reload(query.search, Math.max(0, query.offset - 25))}>Sold Out หน้าก่อนหน้า</button>}
      <SoldOutResults key={query.revision} read={read} search={query.search} offset={query.offset} expanded={expanded}
        onClear={adjust && !currentPending ? item => { setConfirmation(item); dialog.current?.showModal() } : undefined}
        onNext={offset => reload(query.search, offset)} />
      <dialog ref={dialog} aria-labelledby="clear-sold-out-title">
        <h4 id="clear-sold-out-title">ยืนยันการยกเลิก Sold Out</h4>
        <p>{confirmation?.displayName ?? confirmation?.sellableItemId}</p>
        <button className="refresh-button" onClick={() => dialog.current?.close()}>กลับ</button>
        <button className="refresh-button" onClick={() => {
          dialog.current?.close()
          if (confirmation && !currentPending) start(confirmation.sellableItemId, false)
        }}>ยืนยันยกเลิก Sold Out</button>
      </dialog>
    </>}
  </section>
}
