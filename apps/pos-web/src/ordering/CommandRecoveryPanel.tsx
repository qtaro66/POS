import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import { createRecoveryController, type RecoveryReadPort, type RecoveryRow } from './recoveryController.ts'
import type { OrderLine } from '../api/orderingContracts.ts'
import { recoveryStrings } from './recoveryStrings.ts'

function Lookup({ row }: { row: RecoveryRow }) {
  const result = row.outcome?.result
  return <div className="recovery-outcome" role="status">
    {row.phase === 'unchecked' && <p>{row.entry.state === 'resolved' ? recoveryStrings.resolved : recoveryStrings.pending}</p>}
    {row.phase === 'checking' && <p>{recoveryStrings.checking}</p>}
    {row.phase === 'unavailable' && <p>{recoveryStrings.unavailable}</p>}
    {row.phase === 'checked' && result?.kind === 'noDurableResult' && <p>{recoveryStrings.missing}</p>}
    {row.phase === 'checked' && result?.kind === 'durable' && <>
      <p><strong>{result.response.result.status === 'BranchAccepted' ? recoveryStrings.accepted : recoveryStrings.rejected}</strong></p>
      <p>Session ที่สาขายืนยัน: {result.response.result.tableSessionId}</p>
      {result.response.result.errorCode && <p>รหัสผลปฏิเสธ: {result.response.result.errorCode}</p>}
      <p>เวลาที่สาขาบันทึก: <time dateTime={result.response.result.serverReceivedAt!}>{result.response.result.serverReceivedAt}</time></p>
    </>}
    {row.checkedAt && <p>ตรวจสอบเมื่อ: <time dateTime={row.checkedAt}>{row.checkedAt}</time></p>}
    {row.outcome?.recoveryRecorded === false && <p className="recovery-warning">{recoveryStrings.localWarning}</p>}
  </div>
}

function Panel({ port, onClose }: { port: RecoveryReadPort; onClose: () => void }) {
  const controller = useMemo(() => createRecoveryController(port), [port])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus(); void controller.refresh(); return controller.cancel }, [controller])
  useEffect(() => {
    const escape = (event: KeyboardEvent) => { if (event.key === 'Escape' && !event.defaultPrevented) { event.preventDefault(); onClose() } }
    window.addEventListener('keydown', escape)
    return () => { window.removeEventListener('keydown', escape) }
  }, [onClose])
  return <section className="recovery-panel panel" aria-label={recoveryStrings.title}>
    <header><h2 ref={heading} tabIndex={-1}>{recoveryStrings.title}</h2><button onClick={onClose}>{recoveryStrings.close}</button></header>
    <p>{recoveryStrings.note}</p>
    <button className="refresh-button" disabled={state.phase === 'loading'} onClick={() => { void controller.refresh() }}>{recoveryStrings.refresh}</button>
    {(state.phase === 'idle' || state.phase === 'loading') && <p role="status">{recoveryStrings.loading}</p>}
    {state.phase === 'unavailable' && <p role="alert">{recoveryStrings.storageError}</p>}
    {state.phase === 'ready' && state.rows.length === 0 && <p role="status">{recoveryStrings.empty}</p>}
    <div className="recovery-rows">{state.rows.map(row => {
      const command = row.entry.command, body = JSON.parse(command.body)
      return <article className="recovery-row" key={command.requestId} aria-label={`คำขอ ${command.requestId}`}>
        <h3>{command.action === 'OpenTable' ? 'เปิดโต๊ะ' : 'ส่งออเดอร์'}</h3>
        <dl><div><dt>Request ID</dt><dd>{command.requestId}</dd></div>
          <div><dt>Session เดิม</dt><dd>{command.tableSessionId}</dd></div>
          <div><dt>พนักงานเดิม</dt><dd>{command.employeeId}</dd></div>
          <div><dt>เวลาสร้างคำขอ</dt><dd><time dateTime={body.context.clientCreatedAt}>{body.context.clientCreatedAt}</time></dd></div></dl>
        {command.action === 'OpenTable' ? <p>โต๊ะที่ขอเปิด: {body.tableId}</p> : <ul aria-label="รายการในคำขอเดิม">{(body.lines as readonly OrderLine[]).map((line, index) =>
          <li key={index}>รหัสสินค้า {line.sellableItemId} × {line.quantity} · {line.fulfillmentType === 'DineIn' ? 'ทานที่ร้าน' : 'กลับบ้าน'}
            {line.options?.map((option, optionIndex) => <p key={optionIndex}>+ {option.sellableItemId} × {option.quantity} ({option.optionGroupId})</p>)}
            {line.comment && <p>หมายเหตุ: {line.comment}</p>}
          </li>)}</ul>}
        <Lookup row={row} />
        <button className="refresh-button" disabled={row.phase === 'checking'} onClick={() => { void controller.check(command.requestId) }}>{recoveryStrings.check}</button>
      </article>
    })}</div>
  </section>
}

export function CommandRecoveryPanel({ port }: { port: RecoveryReadPort }) {
  const [open, setOpen] = useState(false)
  const trigger = useRef<HTMLButtonElement>(null)
  function close() { setOpen(false); trigger.current?.focus() }
  return <><button ref={trigger} className="refresh-button" aria-expanded={open} onClick={() => { if (open) close(); else setOpen(true) }}>{recoveryStrings.title}</button>
    {open && <Panel port={port} onClose={close} />}</>
}
