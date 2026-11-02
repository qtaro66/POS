import { useEffect, useId, useRef, useSyncExternalStore } from 'react'
import type { createBasketEditor } from './basketEditor.ts'
import type { MenuItem, OrderLine } from '../api/orderingContracts.ts'
import { basketPanelStrings as s, editorIssueLabels, submissionBlockLabels } from './basketPanelStrings.ts'
import './BasketPanel.css'

type Controller = ReturnType<typeof createBasketEditor>
const money = new Intl.NumberFormat('th-TH', { minimumFractionDigits: 2, maximumFractionDigits: 20 })
function Comment({ line, revision, disabled, apply }: { line: OrderLine; revision: number; disabled: boolean; apply: (line: OrderLine, target?: HTMLElement) => Promise<boolean> }) {
  const button = useRef<HTMLButtonElement>(null)
  return <details className="basket-comment"><summary>{s.comment}</summary><form onSubmit={event => {
    event.preventDefault()
    const text = (event.currentTarget.elements.namedItem('comment') as HTMLInputElement).value
    void apply({ ...line, comment: text || null }, button.current ?? undefined)
  }}>
    <label>หมายเหตุ<input key={revision} name="comment" defaultValue={line.comment ?? ''} disabled={disabled} /></label>
    <button ref={button} type="submit" disabled={disabled}>{s.applyComment}</button></form></details>
}

export function BasketPanel({ controller }: { controller: Controller }) {
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const headingId = useId(), result = useRef<HTMLDivElement>(null)
  useEffect(() => { void controller.refresh(); return controller.cancel }, [controller])
  useEffect(() => { if (state.outcome) result.current?.focus() }, [state.outcome])
  const busy = state.activity !== 'idle' || state.phase !== 'ready'
  const editable = !busy && state.issue === null && !state.basket?.pendingRequestId
  const lines = state.basket?.lines ?? [], outcome = state.outcome
  const status = outcome?.kind === 'durable' ? outcome.response.result.status === 'BranchAccepted' ? s.accepted : s.rejected
    : outcome?.kind === 'pending' ? s.pending : outcome?.kind === 'blocked' ? submissionBlockLabels[outcome.reason] : null
  const menu = state.menu && (!state.basket || state.menu.masterDataVersion === state.basket.sessionMasterDataVersion) ? state.menu : null
  const items = new Map(menu?.items.map(item => [item.sellableItemId, item]) ?? [])
  return <section className="basket-panel" aria-labelledby={headingId} aria-busy={busy}>
    <header className="basket-heading"><div><h2 id={headingId}>{s.title} ({lines.length})</h2>{state.detail && <p>โต๊ะ: {state.detail.session.tableId}</p>}</div>
      <button onClick={() => { void controller.refresh() }} disabled={busy}>{s.refresh}</button></header>
    <fieldset className="basket-fulfillment" disabled={!editable}><legend>{s.next}</legend>
      {(['DineIn', 'Takeaway'] as const).map(type => <label key={type}><input type="radio" name={`${headingId}-fulfillment`} checked={state.newFulfillment === type} onChange={() => { controller.setNewFulfillment(type) }} />{type === 'DineIn' ? s.dineIn : s.takeaway}</label>)}</fieldset>
    {state.phase !== 'ready' && <p role="status">{s.loading}</p>}
    {state.issue && !(state.basket?.pendingRequestId && state.issue === 'refreshRequired') && <p className="basket-notice" role="status">{editorIssueLabels[state.issue]}</p>}
    {state.editError && <p className="basket-error" role="alert">{s.editError}</p>}
    {state.activity !== 'idle' && <p role="status">{s[state.activity]}</p>}
    {status && <div ref={result} className={`basket-result ${outcome?.kind === 'durable' ? outcome.response.result.status === 'BranchAccepted' ? 'accepted' : 'rejected' : ''}`} tabIndex={-1} role="status"><strong>{status}</strong>
      {outcome?.kind === 'durable' && <><p>Request ID: {outcome.requestId}</p>{!outcome.localApplied && <p>{s.localWarning}</p>}</>}
      {outcome?.kind === 'blocked' && outcome.requestId && <p>Request ID: {outcome.requestId}</p>}</div>}
    <div className="basket-lines" role="region" aria-label="รายการในตะกร้า" tabIndex={0}>
      {state.phase === 'ready' && !lines.length && <p className="basket-empty">{s.empty}</p>}
      {(['DineIn', 'Takeaway'] as const).map(type => {
        const matching = lines.map((line, index) => ({ line, index })).filter(row => row.line.fulfillmentType === type)
        if (!matching.length) return null
        return <section key={type} aria-label={type === 'DineIn' ? s.dineIn : s.takeaway}><h3>{type === 'DineIn' ? s.dineIn : s.takeaway}</h3>
          {matching.map(({ line, index }) => {
            const item = items.get(line.sellableItemId), name = item?.displayName ?? line.sellableItemId
            const replace = async (next: OrderLine, target?: HTMLElement) => {
              const saved = await controller.replaceLine(index, next)
              if (saved && target?.isConnected && document.activeElement === document.body) target.focus()
              return saved
            }
            return <article className="basket-line" key={`${state.basket!.basketId}:${index}`} aria-label={`รายการ ${index + 1}: ${name}`}>
              <div className="basket-line-title"><strong>{name}</strong><span>{type === 'DineIn' ? s.dineIn : s.takeaway}</span></div>
              <p className="basket-unit">{s.unitPrice}: {item?.unitPrice == null ? '—' : money.format(item.unitPrice)} บาท</p>
              {line.options?.map((option, optionIndex) => {
                const value: MenuItem | undefined = item?.optionGroups.find(group => group.optionGroupId === option.optionGroupId)?.options.find(value => value.sellableItemId === option.sellableItemId)
                return <p className="basket-option" key={optionIndex}>+ {value?.displayName ?? option.sellableItemId} ×{option.quantity} · {value?.unitPrice == null ? '—' : money.format(value.unitPrice)} บาท</p>
              })}
              {line.comment && <p className="basket-saved-comment">หมายเหตุ: {line.comment}</p>}
              <div className="basket-line-actions"><div className="basket-quantity"><button aria-label={`ลดจำนวน ${name}`} disabled={!editable || line.quantity <= 1} onClick={event => { void replace({ ...line, quantity: line.quantity - 1 }, event.currentTarget) }}>−</button>
                <output aria-label={`จำนวน ${name}`}>{line.quantity}</output><button aria-label={`เพิ่มจำนวน ${name}`} disabled={!editable || line.quantity >= 2_147_483_647} onClick={event => { void replace({ ...line, quantity: line.quantity + 1 }, event.currentTarget) }}>+</button></div>
                <label>รับสินค้า<select aria-label={`รับสินค้า ${name}`} disabled={!editable} value={line.fulfillmentType} onChange={event => { void replace({ ...line, fulfillmentType: event.target.value as OrderLine['fulfillmentType'] }) }}><option value="DineIn">{s.dineIn}</option><option value="Takeaway">{s.takeaway}</option></select></label>
                <button className="basket-delete" disabled={!editable} aria-label={`${s.remove} ${name}`} onClick={() => { void controller.removeLine(index) }}>{s.remove}</button></div>
              <Comment revision={state.basket!.revision} line={line} disabled={!editable} apply={replace} />
            </article>
          })}</section>
      })}
    </div>
    <footer className="basket-footer">{!state.basket?.pendingRequestId && <p>{s.confirmedTotals}</p>}
      {state.basket?.pendingRequestId && <><p className="basket-notice">{(!outcome || outcome.kind === 'blocked') && <>{s.pending}<br /></>}Request ID: {state.basket.pendingRequestId}</p>
        <button disabled={busy} onClick={() => { void controller.recover() }}>{s.check}</button><button disabled={busy} onClick={() => { void controller.retry() }}>{s.retry}</button></>}
      {!state.basket?.pendingRequestId && <button className="basket-submit" disabled={!editable || !lines.length} onClick={() => { void controller.submit() }}>ส่งออเดอร์ {lines.length} รายการ</button>}</footer>
  </section>
}
