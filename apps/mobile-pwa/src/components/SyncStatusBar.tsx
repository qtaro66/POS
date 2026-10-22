import { useSyncExternalStore } from 'react'
import type { SyncStatusSource } from '../adapters/ports.ts'
import { countUnsent } from '../domain/outbox.ts'
import { text } from '../strings.ts'
import { Time } from './Time.tsx'

/**
 * Always-visible connection and synchronization summary. Requirement 3.3 asks
 * POS/Mobile to show Offline status and the last employee-data sync time, and
 * to keep the outbox visible because background sending is not guaranteed.
 */
export function SyncStatusBar({ source }: { readonly source: SyncStatusSource }) {
  const status = useSyncExternalStore(
    (listener) => source.subscribe(listener),
    () => source.getStatus(),
  )
  const unsent = countUnsent(status.outbox)
  const online = status.connection === 'online'

  return (
    <section className="sync-bar" aria-label={text.statusRegion}>
      <div role="status" className="sync-bar__items">
        <span className={`pill ${online ? 'pill--online' : 'pill--offline'}`}>
          <span className="pill__dot" aria-hidden="true" />
          {online ? text.branchOnline : text.branchOffline}
        </span>
        <span>
          {status.employeeDataSyncedAt ? (
            <>
              {text.employeeDataSynced} <Time value={status.employeeDataSyncedAt} />
            </>
          ) : (
            text.employeeDataNeverSynced
          )}
        </span>
        <span className={unsent > 0 ? 'sync-bar__outbox--pending' : undefined}>
          {unsent > 0 ? text.outboxCount(unsent) : text.outboxClear}
        </span>
      </div>
      {!online && <p className="sync-bar__notice">{text.offlineNotice}</p>}
    </section>
  )
}
