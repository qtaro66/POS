import { useEffect, useMemo, useRef, useState } from 'react'
import { createCommandJournal } from './commandJournal.ts'
import { createCommandRecovery } from './commandRecovery.ts'
import type { OrderingEntryPort } from './OrderEntryWorkspace.tsx'

export function OpenRegisteredTable({ port, onOpened, refreshVersion }: {
  port: OrderingEntryPort
  refreshVersion: number
  onOpened: (sessionId: string) => Promise<void>
}) {
  const { client, factory } = port
  const recovery = useMemo(() => createCommandRecovery(client, createCommandJournal(factory)), [client, factory])
  const [tables, setTables] = useState<Awaited<ReturnType<typeof client.availableTables>>>([])
  const [pending, setPending] = useState<string>()
  const [message, setMessage] = useState('')
  const [busy, setBusy] = useState(true)
  const locked = useRef(false)

  useEffect(() => {
    const cancel = new AbortController()
    async function load() {
      try {
        const [list, journal] = await Promise.all([client.availableTables(cancel.signal), recovery.list()])
        if (cancel.signal.aborted) return
        setTables(list)
        setPending(journal.find(entry => entry.command.action === 'OpenTable' && entry.command.employeeId === client.employeeId)?.command.requestId)
      } catch {
        if (!cancel.signal.aborted) setMessage('อ่านผังโต๊ะหรือคำขอเปิดโต๊ะไม่ได้')
      } finally {
        if (!cancel.signal.aborted) setBusy(false)
      }
    }
    void load()
    return () => cancel.abort()
  }, [client, recovery, refreshVersion])

  async function open(tableId?: string) {
    if (locked.current) return
    locked.current = true
    setBusy(true)
    setMessage('')
    try {
      const request = pending ? undefined : client.prepareOpenTable(tableId!, crypto.randomUUID())
      const requestId = pending ?? request!.requestId
      setPending(requestId)
      const { result, recoveryRecorded } = request ? await recovery.send(request) : await recovery.retry(requestId)
      if (result.kind !== 'durable' || !recoveryRecorded) {
        setMessage('ยังยืนยันผลเปิดโต๊ะไม่ได้')
        return
      }
      await recovery.finalize(requestId)
      setPending(undefined)
      if (result.response.result.status === 'BranchAccepted' && result.response.result.tableSessionId) {
        await onOpened(result.response.result.tableSessionId)
      } else {
        setMessage('Branch ไม่รับคำขอเปิดโต๊ะ')
      }
      setTables(await client.availableTables())
    } catch {
      setMessage('ตรวจคำขอเปิดโต๊ะไม่ได้ กรุณาเปิดหน้านี้ใหม่')
    } finally {
      locked.current = false
      setBusy(false)
    }
  }

  return <section aria-label="โต๊ะว่าง">
    <h3>โต๊ะว่าง</h3>
    {pending ? <button disabled={busy} onClick={() => { void open() }}>กู้คำขอเปิดโต๊ะเดิม</button>
      : tables.map(table => <button key={table.tableId} disabled={busy} onClick={() => { void open(table.tableId) }}>
        เปิดโต๊ะ {table.displayName}
      </button>)}
    {message && <p role="status">{message}</p>}
  </section>
}
