import { useEffect, useState } from 'react'
import type { ActiveTablesGateway, TableDetail } from '../adapters/ports.ts'
import type { ActiveTablesSnapshot, EmployeeSession, PairedDevice } from '../domain/types.ts'

const money = new Intl.NumberFormat(undefined, { style: 'currency', currency: 'THB' })

interface Props {
  gateway: ActiveTablesGateway
  device: PairedDevice
  employee: EmployeeSession
  sessionId: string
  snapshot: ActiveTablesSnapshot
}

export function TableDetailPanel({ gateway, device, employee, sessionId, snapshot }: Props) {
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
  return (
    <section aria-label="Accepted order">
      <h2>Accepted order</h2>
      {detail.hasOpenCancellationRequest && <p>Cancellation requested</p>}
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
