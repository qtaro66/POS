import { useEffect, useRef, useState } from 'react'
import type { OrderingReadClient } from '../ordering/readController.ts'
import type { CashHistoryProductionItem } from '../api/cashHistoryProductionContracts.ts'
import { parseCashHistoryPrintEvidence, parseCashHistoryPrintAttempts, samePrintJobIdentity, type CashHistoryPrintEvidence as Evidence, type CashHistoryPrintJob, type CashHistoryPrintAttemptsPage } from '../api/cashHistoryPrintContracts.ts'

type Read = OrderingReadClient['cashHistoryPrintEvidence']
type AttemptsRead = OrderingReadClient['cashHistoryPrintAttempts']
interface Props { read: Read; attemptsRead: AttemptsRead; branchId: string; requestId: string; snapshot: CashHistoryProductionItem }
const dates = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
function Time({ value }: { value: string }) { return <time dateTime={value}>{dates.format(new Date(value))}</time> }
const statuses = { PrintQueued: 'รอส่งงานพิมพ์', Connecting: 'กำลังเชื่อมต่อ', Dispatching: 'กำลังส่ง', PrintDispatched: 'ส่งข้อมูลแล้ว', Failed: 'ล้มเหลวก่อนเริ่มส่ง', Unknown: 'ไม่ทราบผล', Completed: 'Completed ตามข้อมูลที่บันทึก' }
function Status({ value }: { value: keyof typeof statuses }) { return <><span>{statuses[value]}</span>{value === 'Unknown' && <strong> · ไม่ทราบผล อาจมีกระดาษออกแล้ว ห้ามส่งซ้ำอัตโนมัติ</strong>}</> }
function Attempts({ read, branchId, requestId, snapshot, job }: { read: AttemptsRead; branchId: string; requestId: string; snapshot: CashHistoryProductionItem; job: CashHistoryPrintJob }) {
  const [revision, setRevision] = useState(0)
  const [state, setState] = useState<{ owner: AttemptsRead; revision: number; page: CashHistoryPrintAttemptsPage | null } | null>(null)
  const [moreState, setMoreState] = useState<{ owner: AttemptsRead; revision: number; loading: boolean; error: string } | null>(null)
  const continuation = useRef<AbortController | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, snapshot.snapshotId, job.printJobId, { limit: 20 }, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let page: CashHistoryPrintAttemptsPage | null = null
      try { if (result.kind === 'ok') { const parsed = parseCashHistoryPrintAttempts(result.value, branchId, requestId, snapshot.snapshotId, job.printJobId, { limit: 20 }, snapshot); if (samePrintJobIdentity(parsed.job, job)) page = parsed } } catch { /* Invalid evidence remains unavailable. */ }
      setState({ owner: read, revision, page })
    }).catch(() => { if (!abort.signal.aborted) setState({ owner: read, revision, page: null }) })
    return () => { abort.abort(); continuation.current?.abort() }
  }, [read, branchId, requestId, snapshot, job, revision])
  const current = state !== null && state.owner === read && state.revision === revision ? state : null, page = current?.page
  const moreLoading = moreState !== null && moreState.owner === read && moreState.revision === revision && moreState.loading
  async function more() {
    if (!read || !page?.nextCursor || moreLoading) return
    continuation.current?.abort()
    const abort = new AbortController(); continuation.current = abort; setMoreState({ owner: read, revision, loading: true, error: '' })
    try {
      const result = await read(requestId, snapshot.snapshotId, job.printJobId, { limit: 20, cursor: page.nextCursor }, abort.signal)
      if (abort.signal.aborted) return
      if (result.kind !== 'ok') throw new Error('Unavailable')
      const next = parseCashHistoryPrintAttempts(result.value, branchId, requestId, snapshot.snapshotId, job.printJobId, { limit: 20 }, snapshot)
      if (!samePrintJobIdentity(next.job, job) || next.nextCursor === page.nextCursor || next.items.some(i => page.items.some(previous => previous.printAttemptId === i.printAttemptId || previous.attemptNumber === i.attemptNumber))) throw new Error('Invalid continuation')
      setState({ owner: read, revision, page: { ...next, items: [...page.items, ...next.items] } })
    } catch { if (!abort.signal.aborted) setMoreState({ owner: read, revision, loading: false, error: 'ยังอ่านครั้งที่พยายามส่งเพิ่มเติมไม่ได้ กรุณาอ่านใหม่' }) }
    finally { if (!abort.signal.aborted) setMoreState(old => old?.owner === read && old.revision === revision ? { ...old, loading: false } : old) }
  }
  return <section aria-label="ครั้งที่พยายามส่งงานพิมพ์"><h6>ครั้งที่พยายามส่ง</h6>{!read ? <p>ยังไม่เชื่อมต่อประวัติครั้งที่พยายามส่งจากบริการสาขารุ่นนี้</p> : <><button type="button" onClick={() => setRevision(v => v + 1)}>อ่านครั้งที่พยายามส่งใหม่</button>
    {!current ? <p role="status">กำลังอ่านครั้งที่พยายามส่ง</p> : !page ? <p role="alert">ยังอ่านครั้งที่พยายามส่งไม่ได้ ข้อมูลรายการและยอดชำระยังคงเดิม</p> : <>
      <p>อ่านหน้าล่าสุดเมื่อ <Time value={page.asOf} /></p><p>สถานะงานที่อ่านครั้งนี้: <Status value={page.job.status} /> · จำนวนครั้งที่งานบันทึก {page.job.attemptCount}</p>
      {page.items.length === 0 ? <p>ไม่พบครั้งที่พยายามส่งที่บันทึกไว้ตามหน้าที่อ่าน ไม่ใช่หลักฐานว่าพิมพ์สำเร็จ</p> : <ol>{page.items.map(attempt => <li key={attempt.printAttemptId}><p>ครั้งที่ {attempt.attemptNumber} · <Status value={attempt.status} /></p>
        <p>เริ่ม <Time value={attempt.startedAt} />{attempt.firstByteAt !== null && <> · เริ่มส่งข้อมูล <Time value={attempt.firstByteAt} /></>}{attempt.finishedAt !== null && <> · จบการบันทึก <Time value={attempt.finishedAt} /></>}</p>
        <p>ค่าการส่งซ้ำอัตโนมัติที่บันทึก: {attempt.automaticRetryAllowed ? 'อนุญาตตามข้อมูลครั้งนั้น' : 'ไม่อนุญาตตามข้อมูลครั้งนั้น'}</p></li>)}</ol>}
      {page.nextCursor && <button type="button" disabled={moreLoading} onClick={() => { void more() }}>{moreLoading ? 'กำลังอ่านครั้งที่พยายามส่งเพิ่มเติม' : 'อ่านครั้งที่พยายามส่งเพิ่มเติม'}</button>}
      {moreState !== null && moreState.owner === read && moreState.revision === revision && moreState.error && <p role="alert">{moreState.error}</p>}
    </>}
    <p>สถานะอาจเปลี่ยนระหว่างอ่านแต่ละหน้า อ่านใหม่เพื่อดูข้อมูลอีกครั้ง ค่าที่บันทึกไม่ใช่สิทธิ์ส่งซ้ำจากหน้านี้</p>
  </>}</section>
}
function Source({ read, attemptsRead, branchId, requestId, snapshot }: Props) {
  const [revision, setRevision] = useState(0)
  const [state, setState] = useState<{ owner: Read; revision: number; evidence: Evidence | null } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(requestId, snapshot.snapshotId, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let evidence: Evidence | null = null
      try { if (result.kind === 'ok') evidence = parseCashHistoryPrintEvidence(result.value, branchId, requestId, snapshot.snapshotId, snapshot) } catch { /* Invalid evidence remains unavailable. */ }
      setState({ owner: read, revision, evidence })
    }).catch(() => { if (!abort.signal.aborted) setState({ owner: read, revision, evidence: null }) })
    return () => abort.abort()
  }, [read, branchId, requestId, snapshot, revision])
  const current = state !== null && state.owner === read && state.revision === revision ? state : null, job = current?.evidence?.job
  return <section aria-label="หลักฐานงานพิมพ์จากข้อมูลตอนส่ง"><h5>งานพิมพ์ที่เกี่ยวข้องกับข้อมูลตอนส่ง</h5>
    {!read ? <p>ยังไม่เชื่อมต่อหลักฐานงานพิมพ์จากบริการสาขารุ่นนี้</p> : <><button type="button" onClick={() => setRevision(v => v + 1)}>อ่านหลักฐานงานพิมพ์ใหม่</button>
      {!current ? <p role="status">กำลังอ่านหลักฐานงานพิมพ์</p> : !current.evidence ? <p role="alert">ยังอ่านหลักฐานงานพิมพ์ไม่ได้ ข้อมูลรายการและยอดชำระยังคงเดิม</p> : !job ? <p>ไม่พบงานพิมพ์ต้นฉบับที่บันทึกไว้ ไม่ใช่หลักฐานว่าไม่มีการส่งออเดอร์หรือพิมพ์สำเร็จ</p> : <>
        <p>อ่านสถานะงานเมื่อ <Time value={current.evidence.asOf} /></p><p>งานต้นฉบับ {job.printJobId} · สถานีเดิม {job.productionStationId} · สร้างเมื่อ <Time value={job.createdAt} /></p><p>สถานะงานที่อ่าน: <Status value={job.status} /> · จำนวนครั้งที่งานบันทึก {job.attemptCount}</p>
        <Attempts key={JSON.stringify([job.printJobId, revision])} read={attemptsRead} branchId={branchId} requestId={requestId} snapshot={snapshot} job={job} />
      </>}
    </>}
    <p>หน้าดูข้อมูลเท่านั้น การส่งข้อมูลสำเร็จหรือสถานะ Completed ที่บันทึกไว้ยังไม่ยืนยันว่ากระดาษออกจริง การอ่านไม่ส่งงานพิมพ์หรือพิมพ์ซ้ำ</p>
  </section>
}
export function CashHistoryPrintEvidence(props: Props) { return <Source key={JSON.stringify([props.branchId, props.requestId, props.snapshot])} {...props} /> }
