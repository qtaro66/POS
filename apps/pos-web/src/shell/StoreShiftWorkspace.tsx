import { ClosingReportQueue } from './ClosingReportQueue.tsx'
import { StoreShiftTimeline } from './StoreShiftTimeline.tsx'
import { useEffect, useRef, useState, type FormEvent } from 'react'
import type { StoreLifecycleCommand, StoreLifecycleState, StoreLifecycleRecovery, StoreOperation } from '../api/storeLifecycleContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'
import './StoreShiftWorkspace.css'
import { readClosingBlockedTables } from '../api/storeLifecycleContracts.ts'

type Port = Required<Pick<OrderingReadClient, 'storeLifecycleCurrent' | 'storeLifecycleCommand'>> & Pick<OrderingReadClient, 'storeLifecycleRequestStatus' | 'retireStoreLifecycleRequest' | 'storeLifecycleWorkspace' | 'storeShiftClosing' | 'closingReportQueueStatus'>
const labels: Record<StoreOperation, string> = { OpenStore: 'เปิดร้าน', OpenShift: 'เปิดกะ', CloseShift: 'ปิดกะ', CloseStore: 'ปิดร้าน' }
const money = (amount: number) => amount.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })

function readPending(key: string): StoreLifecycleCommand | null {
  const text = localStorage.getItem(key)
  if (!text) return null
  const value = JSON.parse(text) as StoreLifecycleCommand
  if (!value || !['OpenStore', 'OpenShift', 'CloseShift', 'CloseStore'].includes(value.operation) ||
      ![value.requestId, value.storeSessionId, value.shiftId].every(id => typeof id === 'string' && /^[a-f0-9-]{36}$/i.test(id)) ||
      !(value.cashAmount === null || typeof value.cashAmount === 'number' && Number.isFinite(value.cashAmount) && value.cashAmount >= 0)) {
    throw new Error('Invalid pending command.')
  }
  return { requestId: value.requestId, storeSessionId: value.storeSessionId, shiftId: value.shiftId,
    operation: value.operation, cashAmount: value.cashAmount }
}

export function StoreShiftWorkspace({ port, storageKey }: { port: Port; storageKey: string }) {
  const [state, setState] = useState<StoreLifecycleState | null>(null)
  const [phase, setPhase] = useState<'loading' | 'ready' | 'unavailable'>('loading')
  const [operation, setOperation] = useState<StoreOperation | null>(null)
  const [cash, setCash] = useState('')
  const [retirement, setRetirement] = useState(false)
  const [message, setMessage] = useState('')
  const [saved] = useState(() => {
    try { return { pending: readPending(storageKey), invalid: false } }
    catch { return { pending: null, invalid: true } }
  })
  const [pending, setPending] = useState<StoreLifecycleCommand | null>(saved.pending)
  const [storageError, setStorageError] = useState(saved.invalid)
  const [reportRefreshKey, setReportRefreshKey] = useState(0)
  const [busy, setBusy] = useState(false)
  const inFlight = useRef(false)
  const alive = useRef(true)
  const dialog = useRef<HTMLDialogElement>(null)
  const retirementDialog = useRef<HTMLDialogElement>(null)

  const readCurrent = port.storeLifecycleCurrent
  useEffect(() => {
    alive.current = true
    const abort = new AbortController()
    void readCurrent(abort.signal).then(result => {
      if (abort.signal.aborted) return
      if (result.kind === 'ok') { setState(result.value); setPhase('ready') }
      else if (result.kind === 'notFound') { setState(null); setPhase('ready') }
      else setPhase('unavailable')
    }).catch(() => { if (!abort.signal.aborted) setPhase('unavailable') })
    return () => { alive.current = false; abort.abort() }
  }, [readCurrent, storageKey])

  useEffect(() => {
    if (operation && dialog.current && !dialog.current.open) dialog.current.showModal()
  }, [operation])

  useEffect(() => {
    if (retirement && retirementDialog.current && !retirementDialog.current.open) retirementDialog.current.showModal()
  }, [retirement])

  async function resolveRecovery(recovery: StoreLifecycleRecovery) {
    if (recovery.status === 'NotFound') {
      setMessage('ยังไม่พบผลคำขอ คำขอเดิมยังคงอยู่ กรุณาตรวจสอบอีกครั้งหรือยกเลิกผ่านสาขา')
      return
    }
    try { localStorage.removeItem(storageKey); setPending(null) }
    catch { setStorageError(true); setMessage('สาขายืนยันผลแล้ว แต่ยังล้างคำขอในเครื่องไม่ได้'); return }
    setOperation(null)
    setRetirement(false)
    const recoveredClose = recovery.status === 'Completed' && pending &&
      (pending.operation === 'CloseShift' || pending.operation === 'CloseStore') ? recovery.result?.closing : null
    const closeSummary = recoveredClose ? ' · ยอดนับจริง ' +
      (recoveredClose.actualCashAmount === null ? 'ไม่ได้กระทบยอด' : money(recoveredClose.actualCashAmount) + ' บาท') +
      ' · ผลต่าง ' + (recoveredClose.cashDifference === null ? 'ไม่ได้กระทบยอด' : money(recoveredClose.cashDifference) + ' บาท') : ''
    setMessage(recovery.status === 'Completed' ? 'สาขาบันทึกคำขอเดิมแล้ว' + closeSummary : 'สาขายืนยันยกเลิกคำขอที่ยังไม่บันทึกแล้ว')
    if (recovery.result) setState(recovery.result)
    if (recovery.status === 'Completed') setReportRefreshKey(previous => previous + 1)
    setPhase('loading')
    try {
      const current = await port.storeLifecycleCurrent()
      if (!alive.current) return
      if (current.kind === 'ok') { setState(current.value); setPhase('ready') }
      else if (current.kind === 'notFound') { setState(null); setPhase('ready') }
      else setPhase('unavailable')
    } catch { if (alive.current) setPhase('unavailable') }
  }

  async function inspectPending() {
    if (!pending || !port.storeLifecycleRequestStatus || inFlight.current || storageError) return
    inFlight.current = true
    setBusy(true)
    try {
      const result = await port.storeLifecycleRequestStatus(pending)
      if (!alive.current) return
      if ('status' in result && result.status === 200) await resolveRecovery(result.body as StoreLifecycleRecovery)
      else setMessage('ยังตรวจสอบผลคำขอไม่ได้ คำขอเดิมยังคงอยู่')
    } catch { if (alive.current) setMessage('ยังตรวจสอบผลคำขอไม่ได้ คำขอเดิมยังคงอยู่') }
    finally { inFlight.current = false; if (alive.current) setBusy(false) }
  }

  async function retirePending(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!pending || !port.retireStoreLifecycleRequest || inFlight.current || storageError) return
    const pinInput = event.currentTarget.elements.namedItem('pin') as HTMLInputElement
    const pin = pinInput.value
    pinInput.value = ''
    inFlight.current = true
    setBusy(true)
    try {
      const result = await port.retireStoreLifecycleRequest(pending, pin)
      if (!alive.current) return
      if ('status' in result && result.status === 200) await resolveRecovery(result.body as StoreLifecycleRecovery)
      else setMessage('สาขายังไม่ยืนยันการยกเลิก คำขอเดิมยังคงอยู่ กรุณาตรวจสอบ PIN หรือเชื่อมต่ออีกครั้ง')
    } catch { if (alive.current) setMessage('สาขายังไม่ยืนยันการยกเลิก คำขอเดิมยังคงอยู่') }
    finally { inFlight.current = false; if (alive.current) setBusy(false) }
  }
  async function refresh() {
    if (inFlight.current) return
    setReportRefreshKey(previous => previous + 1)
    inFlight.current = true
    setBusy(true)
    setPhase('loading')
    try {
      const result = await port.storeLifecycleCurrent()
      if (!alive.current) return
      if (result.kind === 'ok') { setState(result.value); setPhase('ready') }
      else if (result.kind === 'notFound') { setState(null); setPhase('ready') }
      else setPhase('unavailable')
    } catch { if (alive.current) setPhase('unavailable') }
    finally { inFlight.current = false; if (alive.current) setBusy(false) }
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!operation || inFlight.current || storageError) return
    const form = event.currentTarget
    const pinInput = form.elements.namedItem('pin') as HTMLInputElement
    const pin = pinInput.value
    pinInput.value = ''
    const amount = cash.trim() === '' ? null : Number(cash)
    if (!pending && (amount !== null && (!Number.isFinite(amount) || amount < 0 || Math.round(amount * 100) / 100 !== amount) ||
        (operation === 'OpenStore' || operation === 'CloseStore') && amount === null)) return
    const wasPending = pending !== null
    const command = pending ?? {
      requestId: crypto.randomUUID(),
      storeSessionId: operation === 'OpenStore' ? crypto.randomUUID() : state!.storeSessionId,
      shiftId: operation === 'OpenStore' || operation === 'OpenShift' ? crypto.randomUUID() : state!.activeShiftId!,
      operation, cashAmount: amount,
    }
    try { localStorage.setItem(storageKey, JSON.stringify(command)) }
    catch { setStorageError(true); setMessage('บันทึกคำขอในเครื่องไม่ได้ กรุณาตรวจสอบพื้นที่จัดเก็บ'); return }
    setPending(command)
    inFlight.current = true
    setBusy(true)
    try {
      const result = await port.storeLifecycleCommand(command, pin)
      if (!alive.current) return
      if ('status' in result && result.status === 200) {
        setReportRefreshKey(previous => previous + 1)
        setState(result.body as StoreLifecycleState)
        setPhase('ready')
        try { localStorage.removeItem(storageKey); setPending(null) }
        catch { setStorageError(true) }
        setMessage(labels[command.operation] + 'สำเร็จ')
        setOperation(null)
        if (wasPending) {
          setPhase('loading')
          try {
            const current = await port.storeLifecycleCurrent()
            if (!alive.current) return
            if (current.kind === 'ok') { setState(current.value); setPhase('ready') }
            else if (current.kind === 'notFound') { setState(null); setPhase('ready') }
            else setPhase('unavailable')
          } catch { if (alive.current) setPhase('unavailable') }
        }
      } else {
        if (!wasPending && 'status' in result && [400, 401, 403, 409].includes(result.status)) {
          try { localStorage.removeItem(storageKey); setPending(null) } catch { setStorageError(true) }
        }
        const blocked = 'status' in result && result.status === 409 ? readClosingBlockedTables(result.body) : null
        setMessage(blocked ? 'ยังปิดกะหรือร้านไม่ได้ มีรายการชำระบางส่วนที่โต๊ะ ' + blocked.map(table => table.tableId).join(', ') + ' กรุณาแก้รายการจากเมนูโต๊ะก่อนยืนยันอีกครั้ง' :
          'status' in result && result.status === 401 ? 'PIN ไม่ถูกต้องหรือพนักงานไม่ Active กรุณายืนยันคำขอเดิมอีกครั้ง' :
          'status' in result && result.status === 403 ? 'พนักงานหรือเครื่องไม่มีสิทธิ์ดำเนินการ กรุณายืนยันคำขอเดิม' :
          'status' in result && result.status === 409 ? 'คำขอขัดแย้งกับสถานะสาขา กรุณาตรวจสอบสถานะและใช้ PIN ผู้ยืนยันเดิม' :
          'ยังยืนยันผลจากสาขาไม่ได้ กรุณาส่งคำขอเดิมซ้ำด้วย PIN ผู้ยืนยันเดิม')
      }
    } catch {
      if (alive.current) setMessage('ยังยืนยันผลจากสาขาไม่ได้ กรุณาส่งคำขอเดิมซ้ำด้วย PIN ผู้ยืนยันเดิม')
    } finally {
      inFlight.current = false
      if (alive.current) setBusy(false)
    }
  }

  function open(next: StoreOperation) { setCash(''); setMessage(''); setOperation(next) }
  const active = state && !state.isClosed && state.activeShiftId
  const closing = state?.closing
  return <section className="store-workspace" aria-labelledby="store-title">
    <header className="page-header"><h2 id="store-title">ร้านและกะ</h2><button className="refresh-button" onClick={() => { void refresh() }} disabled={busy}>ตรวจสอบสถานะร้าน</button></header>
    {phase === 'loading' && <p role="status">กำลังอ่านสถานะร้าน</p>}
    {phase === 'unavailable' && <p role="alert">ยังอ่านสถานะร้านไม่ได้ กรุณาตรวจสอบสถานะอีกครั้ง</p>}
    {phase === 'ready' && <section className="panel store-state">
      <h3>{!state || state.isClosed ? 'ร้านปิด' : active ? 'ร้านเปิด · มีกะเปิดอยู่' : 'ร้านเปิด · ระหว่างกะ'}</h3>
      {state && <dl><div><dt>Business Date</dt><dd>{state.businessDate}</dd></div><div><dt>เงินสดตั้งต้นร้าน</dt><dd>{money(state.openingCashAmount)} บาท</dd></div>
        <div><dt>กะปัจจุบัน</dt><dd>{state.activeShiftId ?? 'ไม่มีกะเปิดอยู่'}</dd></div></dl>}
      {closing && <div className="store-closing"><h3>{closing.closesStore ? 'ผลปิดร้านล่าสุด' : 'ผลปิดกะล่าสุด'}</h3>
        <dl><div><dt>ยอดที่ควรมี</dt><dd>{money(closing.expectedCashAmount)} บาท</dd></div>
          <div><dt>ยอดนับจริง</dt><dd>{closing.actualCashAmount === null ? 'ไม่ได้กระทบยอด' : money(closing.actualCashAmount) + ' บาท'}</dd></div>
          <div><dt>ผลต่าง</dt><dd>{closing.cashDifference === null ? 'ไม่ได้กระทบยอด' : money(closing.cashDifference) + ' บาท'}</dd></div>
          <div><dt>ผู้ยืนยัน</dt><dd>{closing.context.employeeId}</dd></div></dl></div>}
    </section>}
    <ClosingReportQueue read={port.closingReportQueueStatus} scopeKey={storageKey} refreshKey={reportRefreshKey} />
    {phase === 'ready' && <StoreShiftTimeline port={port} sourceState={state} />}
    {message && <p role="status">{message}</p>}
    {storageError && <p role="alert">ข้อมูลคำขอในเครื่องยังไม่พร้อม กรุณาตรวจสอบก่อนดำเนินการต่อ</p>}
    {pending && <div className="store-pending"><p>มีคำขอ{labels[pending.operation]}ที่ต้องตรวจสอบผล ใช้ PIN ผู้ยืนยันเดิมเพื่อส่งคำขอเดิมซ้ำ</p>
      <button disabled={busy || storageError} onClick={() => { setCash(pending.cashAmount === null ? '' : String(pending.cashAmount)); setOperation(pending.operation) }}>ยืนยันคำขอเดิม</button>
      {port.storeLifecycleRequestStatus && <button disabled={busy || storageError} onClick={() => { void inspectPending() }}>ตรวจสอบผลคำขอ</button>}
      {port.retireStoreLifecycleRequest && <button disabled={busy || storageError} onClick={() => { setMessage(''); setRetirement(true) }}>ยกเลิกคำขอที่ยังไม่บันทึก</button>}
    </div>}
    {phase === 'ready' && !pending && !storageError && <footer className="store-actions">
      {!state || state.isClosed ? <button disabled={busy} onClick={() => open('OpenStore')}>เปิดร้าน</button> :
        active ? <><button disabled={busy} onClick={() => open('CloseShift')}>ปิดกะ</button><button disabled={busy} onClick={() => open('CloseStore')}>ปิดร้าน</button></> :
          <button disabled={busy} onClick={() => open('OpenShift')}>เปิดกะ</button>}
    </footer>}
    {operation && <dialog ref={dialog} className="store-dialog" aria-labelledby="store-operation-title" onCancel={event => { if (busy) event.preventDefault(); else setOperation(null) }}>
      <form onSubmit={event => { void submit(event) }}>
        <h2 id="store-operation-title">{labels[operation]}</h2>
        <p>{operation === 'CloseShift' ? 'ปิดกะนี้โดยร้านยังเปิดอยู่ ยอดนับจริงเว้นว่างได้' : operation === 'CloseStore' ? 'ปิดกะสุดท้ายและปิดร้าน ต้องระบุยอดนับจริงแม้เป็น 0' :
          operation === 'OpenStore' ? 'เปิดร้านพร้อมกะแรก ระบุเงินสดตั้งต้นร้าน' : 'เปิดกะถัดไป เงินตั้งต้นกะเป็นข้อมูลส่งมอบและไม่เปลี่ยนยอดสะสมของร้าน'}</p>
        <label>{operation === 'CloseShift' || operation === 'CloseStore' ? 'ยอดเงินสดนับจริง' : 'เงินสดตั้งต้น'}
          <input type="number" min="0" step="0.01" value={cash} onChange={event => setCash(event.target.value)} readOnly={!!pending} required={operation === 'OpenStore' || operation === 'CloseStore'} /></label>
        {(operation === 'CloseShift' || operation === 'CloseStore') && <p>ตรวจสอบยอดนับจริง {cash === '' ? 'ไม่ได้กระทบยอด' : cash + ' บาท'} เมื่อยืนยันแล้วจะแก้ไขไม่ได้</p>}
        <label>ยืนยัน PIN<input name="pin" type="password" autoComplete="off" inputMode="numeric" required disabled={busy} /></label>
    {message && <p role="status">{message}</p>}
        <div className="store-dialog-actions"><button type="button" disabled={busy} onClick={() => setOperation(null)}>กลับ</button><button disabled={busy}>ยืนยัน{labels[operation]}</button></div>
      </form>
    </dialog>}
    {retirement && pending && <dialog ref={retirementDialog} className="store-dialog" aria-labelledby="store-retirement-title"
      onCancel={event => { if (busy) event.preventDefault(); else setRetirement(false) }}>
      <form onSubmit={event => { void retirePending(event) }}>
        <h2 id="store-retirement-title">ยกเลิกคำขอที่ยังไม่บันทึก</h2>
        <p>สาขาจะตรวจผลคำขอเดิมก่อน หากบันทึกแล้วจะแสดงผลเดิม หากยังไม่บันทึกจะยกเลิกและป้องกันการส่งคำขอเดิมซ้ำ</p>
        <p>คำขอ{labels[pending.operation]} · จำนวนเงิน {pending.cashAmount === null ? 'ไม่ได้ระบุ' : money(pending.cashAmount) + ' บาท'}</p>
        <label>ยืนยัน PIN<input name="pin" type="password" autoComplete="off" inputMode="numeric" required disabled={busy} /></label>
    {message && <p role="status">{message}</p>}
        <div className="store-dialog-actions"><button type="button" disabled={busy} onClick={() => setRetirement(false)}>กลับ</button><button disabled={busy}>ยืนยันยกเลิกคำขอ</button></div>
      </form>
    </dialog>}  </section>
}
