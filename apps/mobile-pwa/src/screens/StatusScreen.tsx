import { useId, useSyncExternalStore } from 'react'
import type { SyncStatusSource } from '../adapters/ports.ts'
import { Screen } from '../components/Screen.tsx'
import { Time } from '../components/Time.tsx'
import type { EmployeeSession, OutboxSummary, PairedDevice } from '../domain/types.ts'
import { text } from '../strings.ts'

interface StatusScreenProps {
  readonly device: PairedDevice
  readonly employee: EmployeeSession
  readonly source: SyncStatusSource
}

const outboxStates: readonly (keyof OutboxSummary)[] = ['draft', 'pending', 'submitted', 'unknown']

/**
 * Connection, employee-data sync and the locally held requests by D20.1
 * state. Mobile keeps the outbox visible because background sending is not
 * guaranteed (requirement 3.3).
 */
export function StatusScreen({ device, employee, source }: StatusScreenProps) {
  const status = useSyncExternalStore(
    (listener) => source.subscribe(listener),
    () => source.getStatus(),
  )
  const connectionId = useId()
  const outboxId = useId()
  const deviceId = useId()

  return (
    <Screen title={text.statusTitle}>
      <section className="panel" aria-labelledby={connectionId}>
        <h2 id={connectionId}>{text.statusConnectionHeading}</h2>
        <dl className="facts">
          <div>
            <dt>{text.statusBranch}</dt>
            <dd>{status.connection === 'online' ? text.branchOnline : text.branchOffline}</dd>
          </div>
          <div>
            <dt>{text.statusEmployeeData}</dt>
            <dd>
              {status.employeeDataSyncedAt ? (
                <Time value={status.employeeDataSyncedAt} />
              ) : (
                text.employeeDataNeverSynced
              )}
            </dd>
          </div>
        </dl>
      </section>

      <section className="panel" aria-labelledby={outboxId}>
        <h2 id={outboxId}>{text.outboxHeading}</h2>
        <dl className="facts">
          {outboxStates.map((state) => (
            <div key={state}>
              <dt>{text.outboxState[state]}</dt>
              <dd>{status.outbox[state]}</dd>
            </div>
          ))}
        </dl>
      </section>

      <section className="panel" aria-labelledby={deviceId}>
        <h2 id={deviceId}>{text.statusDeviceHeading}</h2>
        <dl className="facts">
          <div>
            <dt>{text.statusDeviceName}</dt>
            <dd>{device.deviceName}</dd>
          </div>
          <div>
            <dt>{text.statusBranchName}</dt>
            <dd>{device.branchName}</dd>
          </div>
          <div>
            <dt>{text.statusEmployee}</dt>
            <dd>{employee.displayName}</dd>
          </div>
        </dl>
      </section>
    </Screen>
  )
}
