import type { ClosingReportLifecycle } from '../api/closingReportWorkspaceContracts.ts'

const money = (value: number) => value.toLocaleString('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 2 })
const clock = new Intl.DateTimeFormat('th-TH', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Bangkok' })
function Time({ value }: { value: string }) { return <time dateTime={value}>{clock.format(new Date(value))}</time> }
export function ClosingReportLifecycleView({ value }: { value: ClosingReportLifecycle | null }) {
  if (!value) return <p className="closing-report-legacy-context">รายงานเดิมยังไม่มีบริบทเปิดร้านและรายการกะที่บันทึกไว้ ไม่ดึงข้อมูลปัจจุบันมาเติมย้อนหลัง</p>
  const uncounted = value.shifts.filter(shift => shift.closing.actualCashAmount === null)
  return <section className="closing-report-lifecycle" aria-label="บริบทร้านและกะ ณ เวลาปิด">
    <h5>ร้านและกะ ณ เวลาปิดที่บันทึกไว้</h5>
    <dl className="store-attribution"><div><dt>เงินสดตั้งต้นเปิดร้าน</dt><dd>{money(value.openingCashAmount)} บาท</dd></div>
      <div><dt>ผู้เปิดร้าน</dt><dd>{value.opened.employeeId}</dd></div><div><dt>อุปกรณ์เปิดร้าน</dt><dd>{value.opened.deviceId}</dd></div>
      <div><dt>เวลาเปิดร้าน</dt><dd><Time value={value.opened.occurredAt} /></dd></div><div><dt>จำนวนกะ ณ เวลาปิดนี้</dt><dd>{value.shifts.length} กะ</dd></div></dl>
    <section aria-label="กะที่ไม่ได้กระทบยอด"><h5>กะที่ไม่ได้กระทบยอด {uncounted.length} กะ</h5>
      {uncounted.length ? <ul>{uncounted.map(shift => <li key={shift.shiftId}>{shift.shiftId} · ไม่ได้กระทบยอด (NotReconciled)</li>)}</ul> : <p>ไม่มีกะที่ไม่ได้กระทบยอดในข้อมูลชุดนี้</p>}
    </section>
    <p>เงินเปิดกะแต่ละกะเป็นข้อมูลประกอบ ไม่บวกเพิ่มเข้ายอดเงินสดตั้งต้นของร้าน</p>
    <ol className="closing-report-shifts">{value.shifts.map((shift, index) => <li key={shift.shiftId}><section aria-label={`กะที่บันทึก ${index + 1}`}>
      <h5>กะ {index + 1} · {shift.shiftId}</h5><dl className="store-attribution">
        <div><dt>เงินเปิดกะที่บันทึก</dt><dd>{shift.openingCashAmount === null ? 'ไม่ได้ระบุ' : money(shift.openingCashAmount) + ' บาท'}</dd></div>
        <div><dt>ผู้เปิดกะ</dt><dd>{shift.opened.employeeId}</dd></div><div><dt>อุปกรณ์เปิดกะ</dt><dd>{shift.opened.deviceId}</dd></div>
        <div><dt>เวลาเปิดกะ</dt><dd><Time value={shift.opened.occurredAt} /></dd></div>
        <div><dt>ผู้ปิดกะ</dt><dd>{shift.closing.context.employeeId}</dd></div><div><dt>อุปกรณ์ปิดกะ</dt><dd>{shift.closing.context.deviceId}</dd></div>
        <div><dt>เวลาปิดกะ</dt><dd><Time value={shift.closing.context.occurredAt} /></dd></div>
        <div><dt>ยอดที่ควรมี ณ เวลาปิด</dt><dd>{money(shift.closing.expectedCashAmount)} บาท</dd></div>
        <div><dt>ยอดนับจริง ณ เวลาปิด</dt><dd>{shift.closing.actualCashAmount === null ? 'ไม่ได้กระทบยอด (NotReconciled)' : money(shift.closing.actualCashAmount) + ' บาท'}</dd></div>
        <div><dt>ผลต่าง ณ เวลาปิด</dt><dd>{shift.closing.cashDifference === null ? 'ไม่ได้กระทบยอด' : money(shift.closing.cashDifference) + ' บาท'}</dd></div>
      </dl></section></li>)}</ol>
    <p>ข้อมูลเปิดร้านและกะส่วนนี้เก็บที่สาขา ยังไม่รวมในการส่งไปส่วนกลาง</p>
  </section>
}
