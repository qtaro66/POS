import { useEffect, useRef, useState } from 'react'
import type { StoreLifecycleState, StoreShiftEntry, StoreWorkspace } from '../api/storeLifecycleContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'

type Port = Pick<OrderingReadClient, 'storeLifecycleWorkspace' | 'storeShiftClosing'>
const dateTime = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
const money = (value: number) => value.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
function duration(start: string, end: string) {
  const minutes = Math.floor(Math.max(0, Date.parse(end) - Date.parse(start)) / 60000)
  return Math.floor(minutes / 60) + ' ชม. ' + minutes % 60 + ' นาที'
}
function Timestamp({ value }: { value: string }) { return <time dateTime={value}>{dateTime.format(new Date(value))}</time> }

export function StoreShiftTimeline({ port, sourceState }: { port: Port; sourceState: StoreLifecycleState | null }) {
  const [workspace, setWorkspace] = useState<StoreWorkspace | null>(null)
  const [phase, setPhase] = useState<'loading' | 'ready' | 'unavailable'>('loading')
  const [viewing, setViewing] = useState<{ storeId: string; shiftId: string } | null>(null)
  const [closing, setClosing] = useState<StoreShiftEntry | null>(null)
  const [closingPhase, setClosingPhase] = useState<'loading' | 'ready' | 'unavailable'>('loading')
  const [refresh, setRefresh] = useState(0)
  const closingDialog = useRef<HTMLDialogElement>(null)
  const readWorkspace = port.storeLifecycleWorkspace
  const readClosing = port.storeShiftClosing

  useEffect(() => {
    if (!readWorkspace) return
    const abort = new AbortController()
    setPhase('loading')
    setWorkspace(null)
    void readWorkspace(abort.signal).then(result => {
      if (abort.signal.aborted) return
      if (result.kind === 'ok') { setWorkspace(result.value); setPhase('ready') }
      else setPhase('unavailable')
    }).catch(() => { if (!abort.signal.aborted) setPhase('unavailable') })
    return () => abort.abort()
  }, [readWorkspace, sourceState, refresh])

  useEffect(() => {
    if (!viewing || !readClosing) return
    const abort = new AbortController()
    setClosing(null)
    setClosingPhase('loading')
    if (closingDialog.current && !closingDialog.current.open) closingDialog.current.showModal()
    void readClosing(viewing.storeId, viewing.shiftId, abort.signal).then(result => {
      if (abort.signal.aborted) return
      if (result.kind === 'ok' && result.value.closing) { setClosing(result.value); setClosingPhase('ready') }
      else setClosingPhase('unavailable')
    }).catch(() => { if (!abort.signal.aborted) setClosingPhase('unavailable') })
    return () => abort.abort()
  }, [readClosing, viewing])

  if (!readWorkspace) return null
  const currentStore = workspace?.stores.find(store => store.storeSessionId === workspace.current?.storeSessionId)
  const currentShift = currentStore?.shifts.find(shift => shift.shiftId === workspace?.current?.activeShiftId)
  const recorded = closing?.storeSessionId === viewing?.storeId && closing?.shiftId === viewing?.shiftId ? closing?.closing : null
  const count = workspace?.stores.reduce((sum, store) => sum + store.shifts.length, 0)
  return <section className="panel store-timeline" aria-labelledby="store-timeline-title">
    <header><h3 id="store-timeline-title">กะในวันธุรกิจ</h3><button className="refresh-button" onClick={() => setRefresh(value => value + 1)} disabled={phase === 'loading'}>อ่านประวัติกะอีกครั้ง</button></header>
    {phase === 'loading' && <p role="status">กำลังอ่านประวัติกะ</p>}
    {phase === 'unavailable' && <p role="alert">ยังอ่านประวัติกะไม่ได้</p>}
    {phase === 'ready' && workspace && <>
      <p>Business Date {workspace.businessDate} · จำนวนกะ {count} กะ</p>
      <p>ข้อมูล ณ <Timestamp value={workspace.asOf} /></p>
      {currentStore && <dl className="store-attribution"><div><dt>ผู้เปิดร้าน</dt><dd>{currentStore.opened.employeeId}</dd></div>
        <div><dt>เวลาเปิดร้าน</dt><dd><Timestamp value={currentStore.opened.occurredAt} /></dd></div></dl>}
      {currentShift && <dl className="store-attribution"><div><dt>ผู้เปิดกะปัจจุบัน</dt><dd>{currentShift.opened.employeeId}</dd></div>
        <div><dt>เวลาเปิดกะปัจจุบัน</dt><dd><Timestamp value={currentShift.opened.occurredAt} /></dd></div>
        <div><dt>ระยะเวลากะ ณ เวลาที่อ่าน</dt><dd>{duration(currentShift.opened.occurredAt, workspace.asOf)}</dd></div></dl>}
      {count === 0 && <p>ยังไม่มีกะในวันธุรกิจนี้</p>}
      <ol className="store-shift-list">{workspace.stores.flatMap(store => store.shifts.map(shift =>
        <li key={store.storeSessionId + ':' + shift.shiftId}>
          <h4>กะ {shift.shiftId}</h4>
          <p>{shift.closing ? 'ปิดกะแล้ว' : 'กะเปิดอยู่'} · ผู้เปิด {shift.opened.employeeId}</p>
          <p>เปิด <Timestamp value={shift.opened.occurredAt} /> · ระยะเวลา {duration(shift.opened.occurredAt, shift.closing?.context.occurredAt ?? workspace.asOf)}</p>
          {shift.closing && <><p>ผู้ปิด {shift.closing.context.employeeId} · ปิด <Timestamp value={shift.closing.context.occurredAt} /></p>
            {readClosing && <button className="refresh-button" onClick={() => setViewing({ storeId: store.storeSessionId, shiftId: shift.shiftId })}>ดูผลปิดกะ</button>}</>}
        </li>
      ))}</ol>
    </>}
    {viewing && <dialog ref={closingDialog} className="store-dialog" aria-labelledby="recorded-closing-title" onCancel={() => setViewing(null)}>
      <h2 id="recorded-closing-title">ผลปิดกะที่บันทึกไว้</h2>
      {closingPhase === 'loading' && <p role="status">กำลังอ่านผลปิดกะ</p>}
      {closingPhase === 'unavailable' && <p role="alert">ยังอ่านผลปิดกะนี้ไม่ได้</p>}
      {closingPhase === 'ready' && recorded && <>
        <p>กะ {recorded.shiftId} · {recorded.closesStore ? 'ปิดร้าน' : 'ปิดกะระหว่างวัน'}</p>
        <dl className="store-attribution"><div><dt>ยอดที่ควรมี</dt><dd>{money(recorded.expectedCashAmount)} บาท</dd></div>
          <div><dt>ยอดนับจริง</dt><dd>{recorded.actualCashAmount === null ? 'ไม่ได้กระทบยอด (NotReconciled)' : money(recorded.actualCashAmount) + ' บาท'}</dd></div>
          <div><dt>ผลต่าง</dt><dd>{recorded.cashDifference === null ? 'ไม่ได้กระทบยอด' : money(recorded.cashDifference) + ' บาท'}</dd></div>
          <div><dt>ผู้ยืนยัน</dt><dd>{recorded.context.employeeId}</dd></div>
          <div><dt>เวลายืนยัน</dt><dd><Timestamp value={recorded.context.occurredAt} /></dd></div></dl>
      </>}
      <div className="store-dialog-actions"><button onClick={() => setViewing(null)}>ปิดผลปิดกะ</button></div>
    </dialog>}
  </section>
}
