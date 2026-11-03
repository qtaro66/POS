import { useEffect, useState } from 'react'
import type { SoldOutPage } from '../api/soldOutContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'
import { FreshnessValue } from './DataFreshnessPanel.tsx'

type Read = NonNullable<OrderingReadClient['soldOut']>

function SoldOutResults({ read, search, offset, expanded, onNext }: {
  read: Read; search: string; offset: number; expanded: boolean; onNext: (offset: number) => void
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
    </li>)}</ul>
    {expanded && page.nextOffset !== null &&
      <button className="refresh-button" onClick={() => onNext(page.nextOffset!)}>Sold Out หน้าถัดไป</button>}
  </>
}

export function HomeSoldOut({ read }: { read?: Read }) {
  const [expanded, setExpanded] = useState(false)
  const [draft, setDraft] = useState('')
  const [query, setQuery] = useState({ search: '', offset: 0, revision: 0 })
  function reload(search = '', offset = 0) {
    setQuery(previous => ({ search, offset, revision: previous.revision + 1 }))
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
      <button className="refresh-button" onClick={() => reload(query.search)}>อ่านรายการ Sold Out ใหม่</button>
      {expanded && query.offset > 0 &&
        <button className="refresh-button" onClick={() => reload(query.search, Math.max(0, query.offset - 25))}>Sold Out หน้าก่อนหน้า</button>}
      <SoldOutResults key={query.revision} read={read} search={query.search} offset={query.offset} expanded={expanded}
        onNext={offset => reload(query.search, offset)} />
    </>}
  </section>
}
