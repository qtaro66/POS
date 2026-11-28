import { createContext, useCallback, useContext, useEffect, useId, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react'
import type { createOrderingClient } from '../api/orderingClient.ts'
import { parseKitchenReprintReceipt, parseKitchenReprintStatus, type KitchenReprintReceipt, type KitchenReprintStatus, type KitchenReprintTarget } from '../api/kitchenReprintContracts.ts'
import { createKitchenReprintJournal } from '../ordering/kitchenReprintRecovery.ts'
import type { CashHistoryProductionItem, CashHistoryProductionLine } from '../api/cashHistoryProductionContracts.ts'
import type { CashHistoryPrintJob } from '../api/cashHistoryPrintContracts.ts'

type Client = ReturnType<typeof createOrderingClient>
export type KitchenReprintPort = Pick<Client, 'kitchenReprintScope' | 'kitchenReprint' | 'kitchenReprintStatus'>
type Journal = ReturnType<typeof createKitchenReprintJournal>
type Pending = NonNullable<Awaited<ReturnType<Journal['load']>>>
interface Selection { target: KitchenReprintTarget; snapshot: CashHistoryProductionItem; line: CashHistoryProductionLine }
interface Actions { blocked: boolean; select(value: Selection): void; cancel(target: KitchenReprintTarget): void }
const ReprintActions = createContext<Actions | null>(null)
const messages = { PrintQueued: 'รอส่งงานพิมพ์', Dispatching: 'กำลังส่ง', PrintDispatched: 'ส่งข้อมูลแล้ว ไม่ใช่การยืนยันว่ากระดาษออก', Failed: 'ล้มเหลวก่อนเริ่มส่ง', Unknown: 'ไม่ทราบผล อาจมีกระดาษออกแล้ว ห้ามส่งซ้ำอัตโนมัติ', Completed: 'Completed ตามข้อมูลที่บันทึก' }
const uncertainMessage = 'ยังยืนยันผลคำขอพิมพ์ซ้ำไม่ได้ ห้ามสั่งซ้ำ'

function Confirmation({ selection, busy, error, submit, close }: { selection: Selection; busy: boolean; error: string; submit(reason: string, pin: string): void; close(): void }) {
  const [pin, setPin] = useState(''), [reason, setReason] = useState('')
  const reasonId = useId(), pinId = useId()
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus() }, [])
  const { snapshot, line } = selection
  function send(event: FormEvent) { event.preventDefault(); if (busy) return; const entered = pin; setPin(''); submit(reason, entered) }
  return <form aria-label="ยืนยันสำเนาครัวทีละรายการ" onSubmit={send} className="cash-history-reprint-confirmation">
    <h4 ref={heading} tabIndex={-1}>สำเนา / พิมพ์ซ้ำ</h4><p>พิมพ์เฉพาะรายการเดิมนี้ไปสถานีเดิม ไม่เปลี่ยนออเดอร์หรือยอดชำระ</p>
    <p className="cash-history-recorded-text">{line.displayName} · จำนวนตอนส่ง {line.quantity} · {line.fulfillmentType === 'DineIn' ? 'ทานที่ร้าน' : 'กลับบ้าน'}</p>
    <p>โต๊ะตอนส่ง {snapshot.tableId} · สถานีเดิม {snapshot.productionStationId}</p>
    {line.options.map((option, index) => <p key={index} className="cash-history-recorded-text">+ {option.displayName} ×{option.quantityPerItem} ต่อหน่วย</p>)}
    {line.comment !== null && <p className="cash-history-recorded-text">หมายเหตุเดิม: {line.comment}</p>}
    <label htmlFor={reasonId}>เหตุผลพิมพ์ครัวซ้ำ</label><textarea id={reasonId} required maxLength={4096} disabled={busy} value={reason} onChange={e => setReason(e.target.value)} />
    <label htmlFor={pinId}>PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ</label><input id={pinId} type="password" autoComplete="off" inputMode="numeric" required maxLength={128} disabled={busy} value={pin} onChange={e => setPin(e.target.value)} />
    <p>ยืนยันสิทธิ์เฉพาะรายการนี้ทุกครั้ง โดยไม่เปลี่ยนพนักงานที่เข้าสู่ระบบ</p>
    {error && <p role="alert">{error}</p>}
    <button type="submit" disabled={busy || !pin || !reason.trim()}>ยืนยันพิมพ์สำเนารายการนี้</button>
    <button type="button" disabled={busy} onClick={() => { setPin(''); close() }}>ปิดการยืนยันพิมพ์ซ้ำ</button>
  </form>
}

function Connected({ port, children, journal: suppliedJournal }: { port: KitchenReprintPort; children: ReactNode; journal?: Journal }) {
  const journal = useMemo(() => suppliedJournal ?? createKitchenReprintJournal(port.kitchenReprintScope), [suppliedJournal, port.kitchenReprintScope])
  const [loaded, setLoaded] = useState(false), [selection, setSelection] = useState<Selection | null>(null)
  const [pending, setPending] = useState<Pending | null>(null), [receipt, setReceipt] = useState<KitchenReprintReceipt | null>(null)
  const [status, setStatus] = useState<KitchenReprintStatus | null>(null), [busy, setBusy] = useState(false)
  const [error, setError] = useState(''), [storageError, setStorageError] = useState(false)
  const generation = useRef(0), mounted = useRef(false), inFlight = useRef(false), abort = useRef<AbortController | null>(null)
  const clearRecorded = useCallback(async (commandRequestId: string, current: () => boolean) => {
    const saved = await journal.load()
    if (!current()) return
    if (saved === null) return
    if (saved.commandRequestId !== commandRequestId) throw new Error('Another pending command exists')
    await journal.clear(commandRequestId)
  }, [journal])
  const observe = useCallback(async (entry: Pick<Pending, 'commandRequestId' | 'target'>) => {
    const epoch = generation.current
    const controller = new AbortController(); abort.current?.abort(); abort.current = controller
    const current = () => mounted.current && generation.current === epoch && !controller.signal.aborted
    inFlight.current = true; setBusy(true); setError('')
    try {
      const result = await port.kitchenReprintStatus(entry.commandRequestId, controller.signal)
      if (!current()) return
      if (result.kind !== 'ok') { setError(uncertainMessage); return }
      const parsed = parseKitchenReprintStatus(result.value, port.kitchenReprintScope.branchId, entry.commandRequestId, entry.target)
      setReceipt(parsed.receipt); setStatus(parsed); setSelection(null)
      try { await clearRecorded(entry.commandRequestId, current); if (current()) { setPending(null); setStorageError(false) } } catch { if (current()) { setStorageError(true); setError('ยังปรับข้อมูลกู้สถานะในเครื่องไม่ได้ กรุณาตรวจสถานะเดิมก่อนทำรายการใหม่') } }
    } catch { if (current()) setError(uncertainMessage) }
    finally { if (current()) { inFlight.current = false; setBusy(false) } }
  }, [port, clearRecorded])
  useEffect(() => {
    mounted.current = true
    const epoch = ++generation.current
    const current = () => mounted.current && generation.current === epoch
    void journal.load().then(entry => {
      if (!current()) return
      setPending(entry); setLoaded(true)
      if (entry) void observe(entry)
    }).catch(() => { if (current()) { setStorageError(true); setLoaded(true); setError('ยังอ่านข้อมูลกู้คำขอพิมพ์ซ้ำในเครื่องไม่ได้ จึงยังสั่งพิมพ์ซ้ำไม่ได้') } })
    return () => { generation.current = epoch + 1; mounted.current = false; abort.current?.abort() }
    // Owner changes remount this component; generations also protect effect restarts.
  }, [journal, observe])

  async function submit(reason: string, pin: string) {
    if (!selection || inFlight.current || pending || receipt || storageError || !loaded) return
    inFlight.current = true; setBusy(true); setError('')
    const epoch = generation.current
    const current = () => mounted.current && generation.current === epoch
    const target = selection.target
    let commandRequestId: string
    let entry: Pending
    try {
      commandRequestId = crypto.randomUUID()
      await journal.add({ commandRequestId, target })
      if (!current()) return
      const saved = await journal.load()
      if (!saved || saved.commandRequestId !== commandRequestId) throw new Error('Recovery write unavailable')
      entry = saved
      if (!current()) return
      setPending(entry)
    } catch {
      if (current()) { setStorageError(true); setError('ยังบันทึกข้อมูลกู้คำขอในเครื่องไม่ได้ จึงยังไม่ได้สั่งพิมพ์ซ้ำ'); setBusy(false); inFlight.current = false }
      return
    }
    const controller = new AbortController(); abort.current = controller
    try {
      const result = await port.kitchenReprint(target, commandRequestId, reason, pin, controller.signal)
      pin = ''
      if (!current()) return
      if (result.kind === 'queued') {
        const parsed = parseKitchenReprintReceipt(result.receipt, port.kitchenReprintScope.branchId, commandRequestId, target)
        setReceipt(parsed); setSelection(null)
        await clearRecorded(commandRequestId, current); if (current()) setPending(null)
      } else if (result.kind === 'rejected') {
        await clearRecorded(commandRequestId, current)
        if (current()) { setPending(null); setError(result.httpStatus === 401 ? 'PIN ไม่ผ่าน กรุณายืนยันใหม่' : result.httpStatus === 403 ? 'ไม่มีสิทธิ์พิมพ์ครัวซ้ำ กรุณาใช้ PIN ของผู้มีสิทธิ์' : 'สาขาปฏิเสธคำขอ กรุณาตรวจข้อมูลก่อนยืนยันใหม่') }
      } else { setSelection(null); setError(uncertainMessage) }
    } catch {
      if (current()) { setSelection(null); setError(uncertainMessage) }
    } finally {
      pin = ''
      if (current()) { setBusy(false); inFlight.current = false }
    }
  }

  const blocked = !loaded || busy || !!pending || !!receipt || storageError
  const actions: Actions = {
    blocked,
    select(value) { if (!blocked && !inFlight.current) { setError(''); setSelection(value) } },
    cancel(target) { setSelection(value => value?.target === target ? null : value) },
  }
  const reference = pending ?? (receipt ? { commandRequestId: receipt.requestId, target: { cashRequestId: receipt.cashRequestId, snapshotId: receipt.originalSnapshotId, orderLineId: receipt.orderLineId, originalPrintJobId: receipt.originalPrintJobId, tableSessionId: receipt.tableSessionId, orderBatchId: receipt.orderBatchId, productionStationId: receipt.productionStationId } } : null)
  return <ReprintActions.Provider value={actions}>
    <section aria-label="สถานะพิมพ์ครัวซ้ำ" className="cash-history-reprint-status">
      {!loaded && <p role="status">กำลังอ่านคำขอพิมพ์ซ้ำที่ค้างอยู่</p>}
      {receipt && <><h3>สาขารับคำขอพิมพ์สำเนาแล้ว</h3><p>รายการ {receipt.orderLineId} · สถานีเดิม {receipt.productionStationId}</p><p>คำขอพิมพ์ซ้ำ {receipt.requestId}</p><p>งานสำเนา {receipt.reprintPrintJobId}</p><p>ข้อมูลนี้ไม่ยืนยันว่ากระดาษออกจริง</p></>}
      {status && <p role="status">{messages[status.status]} · จำนวนครั้งที่งานบันทึก {status.attemptCount}</p>}
      {pending && <p>มีคำขอพิมพ์ซ้ำรอตรวจผล {pending.commandRequestId} · รายการ {pending.target.orderLineId}</p>}
      {error && !selection && <p role="alert">{error}</p>}
      {reference && <button type="button" disabled={busy} onClick={() => { if (!inFlight.current) void observe(reference) }}>ตรวจสถานะพิมพ์ครัวซ้ำ</button>}
      {receipt && !pending && !storageError && <button type="button" disabled={busy} onClick={() => { setReceipt(null); setStatus(null); setError('') }}>ปิดสถานะสำเนา</button>}
      {selection && <Confirmation key={JSON.stringify(selection.target)} selection={selection} busy={busy} error={error} submit={(reason, pin) => { void submit(reason, pin) }} close={() => { setSelection(null); setError('') }} />}
    </section>
    {children}
  </ReprintActions.Provider>
}

export function KitchenReprintWorkspace({ port, children, journal }: { port?: KitchenReprintPort; children: ReactNode; journal?: Journal }) {
  const [owner, setOwner] = useState({ port, journal, revision: 0 })
  if (owner.port !== port || owner.journal !== journal) setOwner({ port, journal, revision: owner.revision + 1 })
  return port ? <Connected key={JSON.stringify([port.kitchenReprintScope, owner.revision])} port={port} journal={journal}>{children}</Connected> : <>{children}</>
}

export function KitchenReprintAction({ requestId, snapshot, line, job }: { requestId: string; snapshot: CashHistoryProductionItem; line: CashHistoryProductionLine; job: CashHistoryPrintJob }) {
  const actions = useContext(ReprintActions)
  const target = useMemo<KitchenReprintTarget>(() => ({ cashRequestId: requestId, snapshotId: snapshot.snapshotId, orderLineId: line.orderLineId, originalPrintJobId: job.printJobId,
    tableSessionId: snapshot.tableSessionId, orderBatchId: snapshot.orderBatchId, productionStationId: snapshot.productionStationId }), [requestId, snapshot, line.orderLineId, job.printJobId])
  const cancel = useRef(actions?.cancel)
  useEffect(() => { cancel.current = actions?.cancel }, [actions?.cancel])
  useEffect(() => () => cancel.current?.(target), [target])
  if (!actions) return null
  return <div><p className="cash-history-recorded-text">{line.displayName} · จำนวนตอนส่ง {line.quantity}</p><button type="button" disabled={actions.blocked} onClick={() => actions.select({ target, snapshot, line })}>🔒 พิมพ์ครัวซ้ำรายการนี้</button>{actions.blocked && <p>ตรวจผลหรือปิดสถานะคำขอเดิมก่อนเริ่มรายการใหม่</p>}</div>
}
