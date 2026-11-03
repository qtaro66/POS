import { useEffect, useId, useRef, useState } from 'react'
import type { SoldOutGateway } from '../adapters/ports.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'

type Page = Awaited<ReturnType<SoldOutGateway['read']>>
type Adjustment = Parameters<SoldOutGateway['adjust']>[2]

export function MobileSoldOut({ device, employee, gateway }: {
  device: PairedDevice; employee: EmployeeSession; gateway: SoldOutGateway
}) {
  const id = useId()
  const [query, setQuery] = useState({ offset: 0, revision: 0 })
  const [reading, setReading] = useState<{ query: typeof query; page: Page | null }>()
  const [itemId, setItemId] = useState('')
  const [pending, setPending] = useState<Adjustment>()
  const [outcome, setOutcome] = useState<'saving' | 'saved' | 'rejected' | 'unknown'>()
  const active = useRef<AbortController | undefined>(undefined)
  const page = reading?.query === query ? reading.page : undefined
  const copy = text.soldOut
  useEffect(() => {
    const controller = new AbortController()
    void gateway.read(device, employee, query.offset, controller.signal).then(
      value => { if (!controller.signal.aborted) setReading({ query, page: value }) },
      () => { if (!controller.signal.aborted) setReading({ query, page: null }) },
    )
    return () => controller.abort()
  }, [gateway, device, employee, query])
  useEffect(() => () => { active.current?.abort(); active.current = undefined }, [gateway, device, employee])

  function reload(offset = 0) { setQuery(current => ({ offset, revision: current.revision + 1 })) }
  async function send(adjustment: Adjustment) {
    if (active.current || !page?.canSetSoldOut) return
    const controller = new AbortController()
    active.current = controller
    setPending(adjustment)
    setOutcome('saving')
    const result = await gateway.adjust(device, employee, adjustment, controller.signal).catch(() => 'unknown' as const)
    if (controller.signal.aborted) return
    active.current = undefined
    setOutcome(result)
    if (result !== 'unknown') setPending(undefined)
    if (result === 'saved') setItemId('')
    reload()
  }
  function start(sellableItemId: string, soldOut: boolean) {
    if (pending) return
    void send({ operationId: crypto.randomUUID(), sellableItemId, expectedSoldOut: !soldOut, soldOut })
  }

  return <section className="panel" aria-labelledby={id}>
    <h2 id={id}>{copy.heading}</h2>
    <button type="button" onClick={() => reload()}>{copy.refresh}</button>
    {page === undefined && <p role="status">{copy.loading}</p>}
    {page === null && <p role="alert">{copy.unavailable}</p>}
    {outcome && <p role="status">{copy[outcome]}</p>}
    {page && <>
      {!page.canSetSoldOut && <p>{copy.readOnly}</p>}
      {page.items.length === 0 && <p>{copy.empty}</p>}
      <ul>{page.items.map(item => {
        const name = item.displayName ?? item.sellableItemId
        return <li key={item.sellableItemId}>{name}{page.canSetSoldOut && <button type="button" disabled={!!pending}
          onClick={() => { if (window.confirm(copy.confirm(name))) start(item.sellableItemId, false) }}>{copy.clear(name)}</button>}</li>
      })}</ul>
      {query.offset > 0 && <button type="button" onClick={() => reload(Math.max(0, query.offset - 25))}>{copy.previous}</button>}
      {page.nextOffset !== null && <button type="button" onClick={() => reload(page.nextOffset!)}>{copy.next}</button>}
      {page.canSetSoldOut && <form onSubmit={event => {
        event.preventDefault()
        if (itemId.trim()) start(itemId.trim(), true)
      }}>
        <label>{copy.itemId}<input value={itemId} maxLength={128} onChange={event => setItemId(event.target.value)} /></label>
        <button type="submit" disabled={!itemId.trim() || !!pending}>{copy.set}</button>
      </form>}
      {page.canSetSoldOut && pending && outcome === 'unknown' &&
        <button type="button" onClick={() => { void send(pending) }}>{copy.retry}</button>}
    </>}
  </section>
}
