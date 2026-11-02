import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, useSyncExternalStore, type ReactNode } from 'react'
import type { createOrderingClient } from '../api/orderingClient.ts'
import { createBasketEditor } from './basketEditor.ts'
import { BasketPanel, type DraftLineEdit } from './BasketPanel.tsx'
import { MenuSelector } from './MenuSelector.tsx'
import type { BasketSubmissionResult } from './basketSubmission.ts'
import './OrderEntryWorkspace.css'

export interface OrderingEntryPort { client: ReturnType<typeof createOrderingClient>; factory: IDBFactory }

export function OrderEntryWorkspace({ port, sessionId, sent, onAccepted }: {
  port: OrderingEntryPort; sessionId: string; sent: ReactNode; onAccepted: (outcome: Extract<BasketSubmissionResult, { kind: 'durable' }>) => void
}) {
  const controller = useMemo(() => createBasketEditor(port.client, sessionId, port.factory), [port.client, port.factory, sessionId])
  const state = useSyncExternalStore(controller.subscribe, controller.getSnapshot, controller.getSnapshot)
  const [tab, setTab] = useState<'basket' | 'sent'>('basket')
  const editSequence = useRef(0)
  const [edit, setEdit] = useState<{ owner: typeof controller; target: DraftLineEdit; trigger: HTMLButtonElement; id: number } | null>(null)
  const activeEdit = edit?.owner === controller ? edit : null
  const liveEdit = useRef(activeEdit)
  useLayoutEffect(() => { liveEdit.current = activeEdit }, [activeEdit])
  const id = useId(), acknowledged = useRef<{ owner: typeof controller; requestId: string } | null>(null)
  const tabs = useRef<HTMLDivElement>(null)
  const basketRegion = useRef<HTMLElement>(null)
  useEffect(() => {
    const outcome = state.outcome
    if (outcome?.kind === 'durable' && outcome.response.result.status === 'BranchAccepted'
      && (acknowledged.current?.owner !== controller || acknowledged.current.requestId !== outcome.requestId)) {
      acknowledged.current = { owner: controller, requestId: outcome.requestId }
      onAccepted(outcome)
    }
  }, [state.outcome, controller, onAccepted])
  return <div className="order-entry-workspace">
    <section className="order-entry-left" aria-label="ตะกร้าและรายการที่ส่งแล้ว" ref={basketRegion} tabIndex={-1}>
      <div className="order-entry-tabs" ref={tabs} role="tablist" aria-label="รายการของโต๊ะ" onKeyDown={event => {
        if (event.key === 'ArrowLeft' || event.key === 'ArrowRight' || event.key === 'Home' || event.key === 'End') {
          event.preventDefault()
          const next = event.key === 'Home' ? 'basket' : event.key === 'End' ? 'sent' : tab === 'basket' ? 'sent' : 'basket'
          setTab(next); tabs.current?.querySelector<HTMLButtonElement>(`[data-tab="${next}"]`)?.focus()
        }
      }}>
        {(['basket', 'sent'] as const).map(value => <button key={value} id={`${id}-${value}-tab`} data-tab={value} role="tab" aria-selected={tab === value}
          aria-controls={`${id}-${value}-panel`} tabIndex={tab === value ? 0 : -1} onClick={() => setTab(value)}>{value === 'basket' ? `ตะกร้า (${state.basket?.lines.length ?? 0})` : 'ส่งแล้ว'}</button>)}
      </div>
      <div className="entry-tab-panel" role="tabpanel" hidden={tab !== 'basket'} id={`${id}-basket-panel`} aria-labelledby={`${id}-basket-tab`}><BasketPanel controller={controller}
        editing={activeEdit !== null} onEditLine={(target, trigger) => { setTab('basket'); setEdit({ owner: controller, target, trigger, id: ++editSequence.current }) }} /></div>
      <div className="entry-tab-panel" role="tabpanel" hidden={tab !== 'sent'} id={`${id}-sent-panel`} aria-labelledby={`${id}-sent-tab`}>{sent}</div>
    </section>
    <MenuSelector key={`${sessionId}:${activeEdit?.id ?? 'new'}`} controller={controller} editTarget={activeEdit?.target} editTrigger={activeEdit?.trigger}
      onEndEditing={() => {
        // A late save from an obsolete view must not close or refocus a newer editor.
        if (liveEdit.current !== activeEdit) return
        if (activeEdit && !activeEdit.trigger.isConnected) basketRegion.current?.focus()
        setEdit(null)
      }} onBeginSelection={() => { setTab('basket'); setEdit(null) }} />
  </div>
}
