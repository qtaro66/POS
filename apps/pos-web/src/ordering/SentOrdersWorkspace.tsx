import { useEffect, useMemo, useSyncExternalStore } from 'react'
import type { AcceptedBatch, SessionDetail } from '../api/orderingContracts.ts'
import { createOrderingReadController } from './readController.ts'
import type { OrderingReadClient } from './readController.ts'
import { sessionLabels, strings as s } from './strings.ts'
import { CommandRecoveryPanel } from './CommandRecoveryPanel.tsx'
import type { RecoveryReadPort } from './recoveryController.ts'

const money = new Intl.NumberFormat('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 20 })
const clock = new Intl.DateTimeFormat('th-TH', { hour: '2-digit', minute: '2-digit', second: '2-digit', timeZone: 'Asia/Bangkok' })
function time(utc: string) { return clock.format(new Date(utc)) }

function Batch({ batch, index }: { batch: AcceptedBatch; index: number }) {
  return <article className="sent-batch" aria-labelledby={`batch-${index}`}>
    <header className="batch-header">
      <div><h3 id={`batch-${index}`}>{s.round} {index + 1} <time dateTime={batch.serverReceivedAt}>{time(batch.serverReceivedAt)}</time></h3>
        <p className="batch-reference">{batch.orderBatchId}</p></div>
      <span className="badge accepted">{s.accepted}</span>
    </header>
    {(['DineIn', 'Takeaway'] as const).map(type => {
      const lines = batch.lines.filter(line => line.fulfillmentType === type)
      if (!lines.length) return null
      const label = type === 'DineIn' ? s.dineIn : s.takeaway
      return <section className="fulfillment-group" key={type} aria-label={label}>
        <h4>{label}</h4>
        {lines.map(line => <div className={`sent-line ${line.status === 'Cancelled' ? 'cancelled-line' : ''}`} key={line.orderLineId}>
          <div className="line-body"><div className="line-title"><strong>{line.displayName}</strong><span className={`badge ${type === 'Takeaway' ? 'takeaway' : ''}`}>{label}</span>
            {line.status === 'Cancelled' && <span className="badge cancelled">{s.cancelled}</span>}</div>
            <p>{s.quantity} {line.activeQuantity}{line.originalQuantity !== line.activeQuantity && ` (เดิม ${line.originalQuantity})`} · {s.unitPrice} {money.format(line.unitPrice)}</p>
            {line.options.map((option, optionIndex) => <p className="line-option" key={optionIndex}>+ {option.displayName} ×{option.quantityPerItem} · {money.format(option.unitPrice)}</p>)}
            {line.comment && <p className="line-comment">หมายเหตุ: {line.comment}</p>}
          </div><strong className="line-amount">{money.format(line.activeNetAmount)}</strong>
        </div>)}
      </section>
    })}
    <footer className="batch-footer"><span>{s.batchNet}</span><strong>{money.format(batch.activeItemNetAmount)} บาท</strong></footer>
    <p className="batch-note">{s.batchNote}</p>
  </article>
}

function Totals({ detail }: { detail: SessionDetail }) {
  const totals = detail.billPreviewTotals
  if (!totals) return <div className="empty-state no-active-bill">{s.noActiveBill}</div>
  const rows = [
    [s.itemGross, totals.itemGrossAmount], [s.lineDiscount, totals.lineDiscountAmount],
    [s.billDiscount, totals.billDiscountAmount], [s.serviceCharge, totals.serviceChargeAmount],
    [`${s.vatIncluded} ${totals.vatRate}%`, totals.vatInclusiveAmount], [s.rounding, totals.roundingAdjustment],
  ] as const
  return <section className="bill-summary" aria-label="สรุปยอดจากสาขา">
    <dl>{rows.map(([label, amount]) => <div key={label}><dt>{label}</dt><dd>{money.format(amount)}</dd></div>)}</dl>
    <div className="bill-total"><span>{detail.session.status === 'Ordering' ? s.estimate : s.previewTotal}</span>
      <strong data-testid="net-total">{money.format(totals.netTotal)} บาท</strong></div>
    <p>{s.notReceipt}</p>
  </section>
}

export function SentOrdersWorkspace({ client, branchLabel, preview, onLock, recovery }: {
  client: OrderingReadClient; branchLabel: string; preview: boolean; onLock: () => void; recovery?: RecoveryReadPort
}) {
  const controller = useMemo(() => createOrderingReadController(client), [client])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  const detail = state.tablesPhase === 'ready' && state.detailPhase === 'ready' ? state.detail : null
  return <div className="pos-shell">
    <header className="pos-topbar"><div><span className="brand-mark" aria-hidden="true">▣</span><strong>{branchLabel}</strong><span className="topbar-divider" /><span>PC POS</span></div>
      <button className="lock-button" onClick={onLock}>{s.lock}</button></header>
    {preview && <div className="preview-banner" role="note">{s.preview}</div>}
    <div className="pos-layout">
      <aside className="pos-sidebar"><h1>{s.appTitle}</h1><nav aria-label="เมนูหลัก"><span aria-current="page">▦ &nbsp; โต๊ะ</span></nav><p>ข้อมูลรายการแบบอ่านอย่างเดียว</p></aside>
      <main className="ordering-main">
        <header className="page-header"><div><p className="eyebrow">การขาย / โต๊ะ</p><h2>{s.sent}</h2></div>
          <div className="page-actions">{recovery && <CommandRecoveryPanel port={recovery} />}<button className="refresh-button" onClick={() => { void controller.refresh() }} disabled={state.tablesPhase === 'loading'}>{s.refresh}</button></div></header>
        <div className="ordering-panels">
          <section className="table-panel panel" aria-labelledby="table-list-heading"><header><h2 id="table-list-heading">{s.tables}{state.tablesPhase === 'ready' && ` (${state.tables.length})`}</h2><p>{s.partialFloorPlan}</p></header>
            {(state.tablesPhase === 'idle' || state.tablesPhase === 'loading') && <p className="empty-state" role="status">{s.loadingTables}</p>}
            {state.tablesPhase === 'unavailable' && <p className="error-state" role="alert">{s.tablesError}</p>}
            {state.tablesPhase === 'ready' && (state.tables.length === 0 ? <p className="empty-state">{s.noTables}</p> :
              <div className="table-grid">{state.tables.map(table => <button key={table.tableSessionId}
                className={`table-tile ${state.selectedSessionId === table.tableSessionId ? 'selected' : ''} ${table.status !== 'Ordering' ? 'payment-table' : ''}`}
                aria-pressed={state.selectedSessionId === table.tableSessionId} aria-controls="sent-order-panel"
                onClick={() => { void controller.selectSession(table.tableSessionId) }}>
                <strong>{table.tableId}</strong><span>{sessionLabels[table.status]}</span></button>)}</div>)}
          </section>
          <section id="sent-order-panel" className="detail-panel panel" aria-label={s.sent} aria-busy={state.detailPhase === 'loading'}>
            {state.detailPhase === 'idle' && <p className="empty-state">{s.chooseTable}</p>}
            {state.detailPhase === 'loading' && <p className="empty-state" role="status">{s.loadingDetail}</p>}
            {state.detailPhase === 'notFound' && <p className="empty-state" role="status">{s.notFound}</p>}
            {state.detailPhase === 'unavailable' && <p className="error-state" role="alert">{s.detailError}</p>}
            {detail && <><header className="session-heading"><div><h2>โต๊ะ: {detail.session.tableId}</h2><span className="badge">{sessionLabels[detail.session.status]}</span></div>
              <p>{s.readAt} <time dateTime={detail.asOf}>{time(detail.asOf)}</time></p></header>
              {detail.hasOpenCancellationRequest && <p className="cancellation-notice" role="status">{s.cancellationPending}</p>}
              <div className="batch-list" role="region" aria-label={s.batches} tabIndex={0}>{detail.batches.length === 0 ? <p className="empty-state">{s.noOrders}</p> : detail.batches.map((batch, index) => <Batch key={batch.orderBatchId} batch={batch} index={index} />)}</div>
              <Totals detail={detail} /></>}
          </section>
        </div>
      </main>
    </div>
  </div>
}
