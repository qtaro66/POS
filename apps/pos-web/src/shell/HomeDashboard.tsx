import type { SessionStatus } from '../api/orderingContracts.ts'
import type { OrderingReadState } from '../ordering/readController.ts'
import { sessionLabels } from '../ordering/strings.ts'
import './DataFreshnessPanel.css'

export function HomeDashboard({ state, onRefresh, openTables, openSystem }: {
  state: OrderingReadState; onRefresh: () => void; openTables: (status?: SessionStatus) => void; openSystem: () => void
}) {
  const ready = state.tablesPhase === 'ready'
  const loading = state.tablesPhase === 'idle' || state.tablesPhase === 'loading'
  return <section className="system-status panel" aria-labelledby="home-dashboard-title">
    <header><div><p className="eyebrow">PC POS</p><h2 id="home-dashboard-title">หน้าหลัก</h2></div>
      <button className="refresh-button" onClick={onRefresh} disabled={loading}>อ่านจำนวนโต๊ะใหม่</button></header>
    <h3>สรุปโต๊ะ</h3>
    <p role="status">{ready ? 'จากรายการโต๊ะที่เปิดอยู่ล่าสุด' : loading ? 'กำลังอ่านรายการโต๊ะ' : 'อ่านรายการโต๊ะไม่ได้'}</p>
    <dl className="system-readings">
      <div><dt>โต๊ะว่าง</dt><dd>ยังไม่มีข้อมูลผังโต๊ะ</dd></div>
      {(['Ordering', 'PaymentPending', 'PaymentInProgress', 'PartiallyPaid'] as const).map(status => <div key={status}>
        <dt>{sessionLabels[status]}</dt><dd>{ready ? state.tables.filter(table => table.status === status).length : 'ยังไม่ทราบ'}</dd>
        {ready && <button className="refresh-button" onClick={() => openTables(status)}>ดูโต๊ะ{sessionLabels[status]}</button>}
      </div>)}
    </dl>
    <button className="refresh-button" onClick={() => openTables()}>เปิดรายการโต๊ะ</button>
    <button className="refresh-button" onClick={openSystem}>เปิดสถานะระบบ</button>
  </section>
}
