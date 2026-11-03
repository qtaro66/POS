import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import type { AcceptedBatch, SessionDetail, SessionStatus } from '../api/orderingContracts.ts'
import { createOrderingReadController } from './readController.ts'
import type { OrderingReadClient } from './readController.ts'
import { sessionLabels, strings as s } from './strings.ts'
import { CommandRecoveryPanel } from './CommandRecoveryPanel.tsx'
import type { RecoveryReadPort } from './recoveryController.ts'
import { PcPosShell } from '../shell/PcPosShell.tsx'
import './TableWorkspace.css'
import { OrderEntryWorkspace, type OrderingEntryPort } from './OrderEntryWorkspace.tsx'
import type { BasketSubmissionResult } from './basketSubmission.ts'
import { createDataFreshness } from '../shell/dataFreshness.ts'
import { DataFreshnessPanel, DataFreshnessSidebar } from '../shell/DataFreshnessPanel.tsx'
import { HomeDashboard } from '../shell/HomeDashboard.tsx'

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

function Totals({ detail, compact = false }: { detail: SessionDetail; compact?: boolean }) {
  const totals = detail.billPreviewTotals
  if (!totals) return <div className="empty-state no-active-bill">{s.noActiveBill}</div>
  const rows = [
    [s.itemGross, totals.itemGrossAmount], [s.lineDiscount, totals.lineDiscountAmount],
    [s.billDiscount, totals.billDiscountAmount], [s.serviceCharge, totals.serviceChargeAmount],
    [`${s.vatIncluded} ${totals.vatRate}%`, totals.vatInclusiveAmount], [s.rounding, totals.roundingAdjustment],
  ] as const
  const breakdown = <dl>{rows.map(([label, amount]) => <div key={label}><dt>{label}</dt><dd>{money.format(amount)}</dd></div>)}</dl>
  return <section className="bill-summary" aria-label="สรุปยอดจากสาขา">
    {compact ? <details className="workspace-breakdown"><summary>{s.breakdown}</summary>{breakdown}</details> : breakdown}
    <div className="bill-total"><span>{detail.session.status === 'Ordering' ? s.estimate : s.previewTotal}</span>
      <strong data-testid="net-total">{money.format(totals.netTotal)} บาท</strong></div>
    <p>{s.notReceipt}</p>
  </section>
}

export function SentOrdersWorkspace({ client, branchLabel, preview, onLock, recovery, ordering }: {
  client: OrderingReadClient; branchLabel: string; preview: boolean; onLock: () => void; recovery?: RecoveryReadPort; ordering?: OrderingEntryPort
}) {
  const controller = useMemo(() => createOrderingReadController(client), [client])
  const freshness = useMemo(() => createDataFreshness(client), [client])
  const freshnessState = useSyncExternalStore(freshness.subscribe, freshness.getSnapshot, freshness.getSnapshot)
  useEffect(() => { void freshness.refresh(); return freshness.cancel }, [freshness])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  const [tableFilter, setTableFilter] = useState<SessionStatus>()
  const visibleTables = tableFilter ? state.tables.filter(table => table.status === tableFilter) : state.tables
  const [entry, setEntry] = useState<{ owner: typeof controller; sessionId: string } | null>(null)
  const inSession = entry?.owner === controller && entry.sessionId === state.selectedSessionId
  const selectionVisible = inSession || !tableFilter || visibleTables.some(table => table.tableSessionId === state.selectedSessionId)
  const detail = selectionVisible && state.tablesPhase === 'ready' && state.detailPhase === 'ready' ? state.detail : null
  const heading = useRef<HTMLHeadingElement>(null), moveFocus = useRef(false)
  const [receipt, setReceipt] = useState<{ owner: OrderingReadClient; outcome: Extract<BasketSubmissionResult, { kind: 'durable' }> } | null>(null)
  useEffect(() => {
    if (moveFocus.current) { heading.current?.focus(); moveFocus.current = false }
  }, [inSession])
  const sentPanel = <section key="detail" id="sent-order-panel" className="detail-panel panel" aria-label={s.sent} aria-busy={selectionVisible && state.detailPhase === 'loading'}>
    {(!selectionVisible || state.detailPhase === 'idle') && <p className="empty-state">{s.chooseTable}</p>}
    {selectionVisible && state.detailPhase === 'loading' && <p className="empty-state" role="status">{s.loadingDetail}</p>}
    {selectionVisible && state.detailPhase === 'notFound' && <p className="empty-state" role="status">{s.notFound}</p>}
    {selectionVisible && state.detailPhase === 'unavailable' && <p className="error-state" role="alert">{s.detailError}</p>}
    {detail && <><header className="session-heading"><div><h2>โต๊ะ: {detail.session.tableId}</h2><span className="badge">{sessionLabels[detail.session.status]}</span></div>
      <p>{s.readAt} <time dateTime={detail.asOf}>{time(detail.asOf)}</time></p>
      {!inSession && <button className="workspace-open" onClick={() => { moveFocus.current = true; setEntry({ owner: controller, sessionId: detail.session.tableSessionId }) }}>{s.openTableWorkspace}</button>}</header>
      {detail.hasOpenCancellationRequest && <p className="cancellation-notice" role="status">{s.cancellationPending}</p>}
      <div className="batch-list" role="region" aria-label={s.batches} tabIndex={0}>{detail.batches.length === 0 ? <p className="empty-state">{s.noOrders}</p> : detail.batches.map((batch, index) => <Batch key={batch.orderBatchId} batch={batch} index={index} />)}</div>
      <Totals detail={detail} compact={inSession} /></>}
  </section>
  return <PcPosShell branchLabel={branchLabel} preview={preview} onLock={onLock}
    sidebarStatus={<DataFreshnessSidebar state={freshnessState} />}
    homePage={(openTables, openSystem) => <HomeDashboard state={state} onRefresh={() => { void controller.refresh() }}
      openTables={status => { setTableFilter(status); setEntry(null); openTables() }} openSystem={openSystem} client={client} freshness={freshnessState} canAdjust={!preview} />}
    systemPage={<DataFreshnessPanel state={freshnessState} onRefresh={() => { void freshness.refresh() }} printPort={client} outboxPort={client} replayPort={client} printerPort={client} readinessRead={client.branchReadiness} />}>
        <header className={`page-header ${inSession && ordering ? 'entry-page-header' : ''}`}><div><p className="eyebrow">การขาย / โต๊ะ</p><h2 ref={heading} tabIndex={-1}>{inSession ? s.tableWorkspace : s.sent}</h2></div>
          <div className="page-actions">{recovery && <CommandRecoveryPanel port={recovery} />}<button className="refresh-button" onClick={() => { void controller.refresh() }} disabled={state.tablesPhase === 'loading'}>{s.refresh}</button></div></header>
        {inSession && <button className="workspace-back" onClick={() => { moveFocus.current = true; setEntry(null) }}>{s.backToTables}</button>}
        {receipt?.owner === client && <div className="workspace-receipt" role="status"><strong>สาขารับออร์เดอร์แล้ว</strong> · Request ID: {receipt.outcome.requestId}
          {!receipt.outcome.localApplied && <p>ข้อมูลในเครื่องยังปรับปรุงไม่ครบ กรุณาตรวจผลคำขอเดิม ห้ามสร้างออร์เดอร์ใหม่แทน</p>}
          <button aria-label="ปิดข้อความรับออร์เดอร์" onClick={() => setReceipt(null)}>×</button></div>}
        {inSession && ordering ? <OrderEntryWorkspace port={ordering} sessionId={state.selectedSessionId!} sent={sentPanel} onAccepted={outcome => {
          setReceipt({ owner: client, outcome }); void controller.refresh()
        }} /> : <div className={inSession ? 'session-ordering-panels' : 'ordering-panels'}>
          {!inSession && <section key="tables" className="table-panel panel" aria-labelledby="table-list-heading"><header><h2 id="table-list-heading">{s.tables}{state.tablesPhase === 'ready' && ` (${visibleTables.length})`}</h2><p>{s.partialFloorPlan}</p>
            {tableFilter && <p>สถานะ: {sessionLabels[tableFilter]} <button className="refresh-button" onClick={() => setTableFilter(undefined)}>แสดงโต๊ะที่เปิดอยู่ทั้งหมด</button></p>}</header>
            {(state.tablesPhase === 'idle' || state.tablesPhase === 'loading') && <p className="empty-state" role="status">{s.loadingTables}</p>}
            {state.tablesPhase === 'unavailable' && <p className="error-state" role="alert">{s.tablesError}</p>}
            {state.tablesPhase === 'ready' && (visibleTables.length === 0 ? <p className="empty-state">{tableFilter ? "ไม่มีโต๊ะในสถานะนี้" : s.noTables}</p> :
              <div className="table-grid">{visibleTables.map(table => <button key={table.tableSessionId}
                className={`table-tile ${state.selectedSessionId === table.tableSessionId ? 'selected' : ''} ${table.status !== 'Ordering' ? 'payment-table' : ''}`}
                aria-pressed={state.selectedSessionId === table.tableSessionId} aria-controls="sent-order-panel"
                onClick={() => { void controller.selectSession(table.tableSessionId) }}>
                <strong>{table.tableId}</strong><span>{sessionLabels[table.status]}</span></button>)}</div>)}
          </section>}
          {sentPanel}
          {inSession && <section key="menu" className="menu-unavailable panel" aria-labelledby="menu-placeholder-heading">
            <h2 id="menu-placeholder-heading">{s.menu}</h2><p role="status">{s.menuUnavailable}</p>
            <p>{s.entryReadOnly}</p>
          </section>}
        </div>}
  </PcPosShell>
}
