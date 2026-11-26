import { useEffect, useState } from 'react'
import type { QueryResult } from '../api/orderingClient.ts'
import type { CashHistoryItem } from '../api/cashHistoryContracts.ts'
import { parseCashHistoryShiftContext, parseCashHistoryReceiptEvidence } from '../api/cashHistoryContextContracts.ts'
import type { OrderingReadClient } from '../ordering/readController.ts'

const dates = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
const money = (v: number) => v.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 20 })
function Time({ value }: { value: string }) { return <time dateTime={value}>{dates.format(new Date(value))}</time> }
type Reader<T> = ((requestId: string, signal?: AbortSignal) => Promise<QueryResult<T>>) | undefined
function useEvidence<T>(read: Reader<T>, branchId: string, item: CashHistoryItem, parse: (value: unknown, branch: string, request: string, captured: CashHistoryItem) => T) {
  const [revision, setRevision] = useState(0)
  const [state, setState] = useState<{ owner: Reader<T>; branch: string; item: CashHistoryItem; revision: number; value: T | null } | null>(null)
  useEffect(() => {
    const abort = new AbortController()
    if (read) void read(item.requestId, abort.signal).then(result => {
      if (abort.signal.aborted) return
      let value: T | null = null
      try { if (result.kind === 'ok') value = parse(result.value, branchId, item.requestId, item) } catch { /* Invalid evidence stays unavailable. */ }
      setState({ owner: read, branch: branchId, item, revision, value })
    }).catch(() => { if (!abort.signal.aborted) setState({ owner: read, branch: branchId, item, revision, value: null }) })
    return () => abort.abort()
  }, [read, branchId, item, revision, parse])
  return { current: state !== null && state.owner === read && state.branch === branchId && state.item === item && state.revision === revision ? state : null, refresh: () => setRevision(v => v + 1) }
}
function Shift({ read, branchId, item }: { read: OrderingReadClient['cashHistoryShiftContext']; branchId: string; item: CashHistoryItem }) {
  const { current, refresh } = useEvidence(read, branchId, item, parseCashHistoryShiftContext)
  const labels = { OpenStore: 'เปิดร้านและกะแรก', OpenShift: 'เปิดกะ', CloseShift: 'ปิดกะ', CloseStore: 'ปิดร้านและกะ' }
  return <section aria-label="หลักฐานรอบร้านและกะ"><h4>รอบร้านและกะที่เกี่ยวข้อง</h4>
    <p>รอบร้าน {item.storeSessionId} · กะ {item.shiftId} · วันธุรกิจ {item.businessDate}</p>
    {!read ? <p>ยังไม่เชื่อมต่อหลักฐานรอบร้านและกะจากบริการสาขารุ่นนี้</p> : <><button type="button" onClick={refresh}>อ่านหลักฐานกะใหม่</button>
      {!current ? <p role="status">กำลังอ่านหลักฐานรอบร้านและกะ</p> : !current.value ? <p role="alert">ยังอ่านหลักฐานรอบร้านและกะไม่ได้ ยอดชำระที่บันทึกยังคงเดิม</p> : current.value.entries.length === 0 ? <p>ไม่พบหลักฐานรอบร้านและกะที่บันทึกไว้สำหรับรายการนี้ ไม่ได้ยืนยันว่ากะยังเปิดหรือปิดแล้ว</p> : <ol>{current.value.entries.map(entry => <li key={entry.requestId}>
        <h5>{labels[entry.action]}</h5><p>บันทึกเมื่อ <Time value={entry.occurredAt} /> · วันที่สาขาที่บันทึก {entry.branchLocalDate}</p><p>พนักงาน {entry.employeeId} · อุปกรณ์ที่บันทึก {entry.deviceId} · คำขอเดิม {entry.requestId}</p>
        {entry.closing ? <><p>เงินสดคาดหมายที่บันทึก {money(entry.closing.expectedCashAmount)} บาท</p>{entry.closing.actualCashAmount === null ? <p>ไม่ได้บันทึกยอดนับเงินสด จึงยังไม่มียอดต่างจากการกระทบยอด</p> : <p>เงินสดที่นับได้ {money(entry.closing.actualCashAmount)} บาท · ยอดต่างที่บันทึก {money(entry.closing.cashDifference!)} บาท</p>}</> : <p>{entry.cashAmount === null ? 'ไม่ได้บันทึกเงินสดเปิดกะ' : `เงินสดเปิดกะที่บันทึก ${money(entry.cashAmount)} บาท`}</p>}
      </li>)}</ol>}
      <p>แสดงเหตุการณ์ของกะที่ผูกกับรายการเดิม ไม่ใช่สถานะกะปัจจุบัน ยอดปิดกะเป็นหลักฐานทั้งกะ ไม่ใช่ยอดของใบเสร็จนี้ใบเดียว</p>
    </>}
  </section>
}
function Receipt({ read, branchId, item }: { read: OrderingReadClient['cashHistoryReceiptEvidence']; branchId: string; item: CashHistoryItem }) {
  const { current, refresh } = useEvidence(read, branchId, item, parseCashHistoryReceiptEvidence)
  return <section aria-label="หลักฐานผลจำลองใบเสร็จ"><h4>ผลจำลองใบเสร็จที่บันทึกไว้</h4>
    {!read ? <p>ยังไม่เชื่อมต่อหลักฐานใบเสร็จจากบริการสาขารุ่นนี้</p> : <><button type="button" onClick={refresh}>อ่านหลักฐานใบเสร็จใหม่</button>
      {!current ? <p role="status">กำลังอ่านหลักฐานใบเสร็จ</p> : !current.value ? <p role="alert">ยังอ่านหลักฐานใบเสร็จไม่ได้ ยอดชำระที่บันทึกยังคงเดิม</p> : <>
        <p>ใบเสร็จ {current.value.receiptNumber} · บันทึกรอจำลองเมื่อ <Time value={current.value.queuedAt} /></p>
        {current.value.simulation === null ? <p>ยังไม่มีผลจำลองใบเสร็จที่บันทึกไว้</p> : current.value.simulation.status === 'Failed' ? <p>ผลจำลองที่บันทึก: จำลองไม่สำเร็จ</p> : <><p>ผลจำลองที่บันทึก: จำลองสำเร็จ</p><pre className="cash-history-recorded-text" aria-label="ข้อความใบเสร็จจำลองเดิม">{current.value.simulation.text}</pre></>}
      </>}
      <p>ผลจำลองไม่ใช่หลักฐานว่าพิมพ์จริงสำเร็จ การเปิดดูไม่สร้างหรือส่งงานพิมพ์และไม่เปิดลิ้นชักเงิน</p>
    </>}
  </section>
}
export function CashHistoryContextPanels({ shiftRead, receiptRead, branchId, item }: { shiftRead: OrderingReadClient['cashHistoryShiftContext']; receiptRead: OrderingReadClient['cashHistoryReceiptEvidence']; branchId: string; item: CashHistoryItem }) {
  return <><Shift read={shiftRead} branchId={branchId} item={item} /><Receipt read={receiptRead} branchId={branchId} item={item} /></>
}
