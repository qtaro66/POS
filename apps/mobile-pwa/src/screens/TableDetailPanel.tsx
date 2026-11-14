import { useEffect, useState, type FormEvent } from 'react'
import type { ActiveTablesGateway, TableDetail } from '../adapters/ports.ts'
import type { ActiveTablesSnapshot, EmployeeSession, PairedDevice } from '../domain/types.ts'

const money = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'THB' })

const cancellationLabels: Readonly<Record<string, string>> = {
  CancellationRequested: 'Waiting for cashier', ProductionStatusChecking: 'Checking production',
  StatusRecorded: 'Inspection recorded', Confirmed: 'Confirmed', PartiallyConfirmed: 'Partially confirmed',
  Rejected: 'Rejected',
}

interface Props {
  onCancellation?: (lines: readonly import('../domain/mobileRequest.ts').CancellationSelection[], reason?: string) => Promise<boolean>
  cancellationBlocked?: boolean
  onRequestPayment?: () => void
  paymentBlocked?: boolean
  gateway: ActiveTablesGateway
  device: PairedDevice
  employee: EmployeeSession
  sessionId: string
  snapshot: ActiveTablesSnapshot
}

export function TableDetailPanel({ gateway, device, employee, sessionId, snapshot, onRequestPayment, paymentBlocked, onCancellation, cancellationBlocked }: Props) {
  const version = snapshot.tables.find(row => row.tableSessionId === sessionId)?.tableVersion
  const [result, setResult] = useState<{ snapshot: ActiveTablesSnapshot; detail: TableDetail | null }>()
  useEffect(() => {
    const controller = new AbortController()
    function apply(detail: TableDetail | null) {
      if (!controller.signal.aborted) setResult({ snapshot, detail })
    }
    void gateway.loadDetail?.(device, employee, sessionId, controller.signal).then(apply, () => apply(null))
    return () => controller.abort()
  }, [gateway, device, employee, sessionId, snapshot])

  const detail = result?.detail
  if (result?.snapshot !== snapshot && (!detail || detail.tableVersion !== version)) {
    return <p role="status">Loading table details…</p>
  }
  if (!detail || detail.tableVersion < (version ?? 0)) {
    return <p role="status">Table details are unavailable from the branch.</p>
  }
  const requests = detail.cancellationRequests ?? []
  const pending = requests.filter(request => ['CancellationRequested', 'ProductionStatusChecking', 'StatusRecorded'].includes(request.status))
  const cancellable = detail.lines.filter(line => line.status === 'Active').map(line => ({
    ...line, available: line.quantity - pending.reduce((sum, request) => sum + (request.quantities[line.id] ?? 0), 0),
  })).filter(line => line.available > 0)
  async function requestCancellation(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!onCancellation || cancellationBlocked) return
    const form = event.currentTarget
    const data = new FormData(form)
    const lines = cancellable.map(line => ({ orderLineId: line.id, quantity: Number(data.get(line.id)) }))
      .filter(line => line.quantity > 0)
    if (lines.length && await onCancellation(lines, String(data.get("cancellationReason") ?? "").trim() || undefined)) form.reset()
  }
  return (
    <section aria-label="Accepted order">
      <h2>Accepted order</h2>
      {requests.length > 0 && <ul aria-label="Cancellation requests">
        {requests.map(request => <li key={request.id}>{request.id} · {cancellationLabels[request.status] ?? request.status}</li>)}
      </ul>}
      {detail.hasOpenCancellationRequest && requests.length === 0 && <p>Cancellation requested</p>}
      {detail.lines.length === 0 ? <p>No accepted items.</p> : (
        <ul>
          {detail.lines.map(line => (
            <li key={line.id}>
              <strong>{line.name}</strong> · {line.quantity} × {money.format(line.unitPrice)} · {line.status}
              {line.options.map((option, index) => (
                <div key={index}>{option.name} · {option.quantity} × {money.format(option.unitPrice)}</div>
              ))}
              {line.comment && <div>{line.comment}</div>}
              {line.discount !== 0 && <div>Discount: {money.format(line.discount)}</div>}
              <div>Line total: {money.format(line.total)}</div>
            </li>
          ))}
        </ul>
      )}
      {onRequestPayment && detail.status === 'Ordering' && !detail.hasOpenCancellationRequest &&
        detail.lines.some(line => line.quantity > 0) && (
          <button type="button" className="button" disabled={paymentBlocked || result?.snapshot !== snapshot || detail.tableVersion !== version}
            onClick={onRequestPayment}>Request payment</button>
        )}
      {onCancellation && detail.canRequestCancellation && detail.status === 'Ordering' && cancellable.length > 0 && (
        <form onSubmit={event => { void requestCancellation(event) }}>
          <fieldset disabled={cancellationBlocked || result?.snapshot !== snapshot || detail.tableVersion !== version}>
            <legend>Request cancellation</legend>
            {cancellable.map(line => <label key={line.id}>{line.name}
              <input type="number" name={line.id} min={0} max={line.available} step={1} defaultValue={0} required />
            </label>)}
            <label>Reason (optional)<input name="cancellationReason" maxLength={500} /></label>
            <button type="submit" className="button">Send cancellation request</button>
          </fieldset>
        </form>
      )}
      {detail.totals && (
        <>
          <h3>{detail.status === 'Ordering' ? 'Current estimate' : 'Bill totals'}</h3>
          <dl className="facts">
            {Object.entries(detail.totals).map(([label, amount]) => (
              <div key={label}><dt>{label}</dt><dd>{money.format(amount)}</dd></div>
            ))}
          </dl>
        </>
      )}
    </section>
  )
}
