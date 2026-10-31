import { useEffect, useMemo, useRef, useState, useSyncExternalStore } from 'react'
import type { createOrderingClient } from './api/orderingClient.ts'
import type { CommandJournal } from './ordering/commandJournal.ts'
import { createCommandRecovery } from './ordering/commandRecovery.ts'
import { SentOrdersWorkspace } from './ordering/SentOrdersWorkspace.tsx'
import type { OrderingReadClient } from './ordering/readController.ts'
import { strings } from './ordering/strings.ts'
import './App.css'

function createScreenLocks() {
  const clients = new WeakSet<OrderingReadClient>(), listeners = new Set<() => void>()
  return {
    subscribe(listener: () => void) { listeners.add(listener); return () => { listeners.delete(listener) } },
    isLocked(client: OrderingReadClient) { return clients.has(client) },
    lock(client: OrderingReadClient) { clients.add(client); for (const listener of listeners) listener() },
  }
}

function SignInRequired() {
  const heading = useRef<HTMLHeadingElement>(null)
  useEffect(() => { heading.current?.focus() }, [])
  return <main className="pos-locked"><span aria-hidden="true">▣</span><h1>{strings.appTitle}</h1>
    <h2 ref={heading} tabIndex={-1}>{strings.signInTitle}</h2><p>{strings.signInHint}</p></main>
}

type AppProps = { branchLabel?: string; preview?: boolean } & (
  { client?: OrderingReadClient; journal?: never } |
  { client: ReturnType<typeof createOrderingClient>; journal: CommandJournal }
)

function App(props: AppProps) {
  const { client, branchLabel = 'สาขา', preview = false } = props
  const recovery = useMemo(() => {
    if (!props.journal) return undefined
    const adapter = createCommandRecovery(props.client, props.journal)
    return { list: adapter.list, recover: adapter.recover }
  }, [props.client, props.journal])
  const [locks] = useState(createScreenLocks)
  const locked = useSyncExternalStore(locks.subscribe, () => !client || locks.isLocked(client), () => true)
  if (!client || locked) return <SignInRequired />
  return <SentOrdersWorkspace client={client} branchLabel={branchLabel} preview={preview} recovery={recovery} onLock={() => { locks.lock(client) }} />
}

export default App

