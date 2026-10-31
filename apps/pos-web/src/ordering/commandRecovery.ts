import { type createOrderingClient, type CommandQueryResult, type CommandResult, type CommandSnapshot, type PreparedCommand } from '../api/orderingClient.ts'
import type { CommandJournal } from './commandJournal.ts'

type Client = ReturnType<typeof createOrderingClient>
export interface RecoveryOutcome<T> { readonly result: T; readonly recoveryRecorded: boolean }

/** Explicit calls only. Construction, reload and listing never send a mutation. */
export function createCommandRecovery(client: Client, journal: CommandJournal) {
  async function load(requestId: string) {
    const saved = await journal.load(client.scope, requestId)
    if (!saved) throw new Error('Command recovery entry is missing.')
    return saved
  }
  async function record<T extends CommandResult | CommandQueryResult>(command: CommandSnapshot, result: T): Promise<RecoveryOutcome<T>> {
    if (result.kind !== 'durable') return { result, recoveryRecorded: true }
    try { await journal.resolve(command); return { result, recoveryRecorded: true } }
    catch { return { result, recoveryRecorded: false } }
  }
  async function lookup(command: CommandSnapshot, signal?: AbortSignal): Promise<RecoveryOutcome<CommandQueryResult>> {
    const result = await client.commandStatus(command.requestId, signal)
    if (result.kind === 'durable' && command.action === 'SubmitOrder' && result.response.result.tableSessionId !== command.tableSessionId) {
      return { result: { kind: 'unavailable', reason: 'protocol' }, recoveryRecorded: true }
    }
    return record(command, result)
  }
  return {
    list: () => journal.list(client.scope),
    async send(command: PreparedCommand, signal?: AbortSignal): Promise<RecoveryOutcome<CommandResult>> {
      const snapshot = client.snapshot(command)
      // Commit the request before any network operation. Never overwrite an existing ID.
      if (!await journal.add(snapshot)) throw new Error('Command already journaled; recover or explicitly retry it.')
      return record(snapshot, await client.execute(command, signal))
    },
    async recover(requestId: string, signal?: AbortSignal) {
      return lookup((await load(requestId)).command, signal)
    },
    async retry(requestId: string, signal?: AbortSignal): Promise<RecoveryOutcome<CommandResult | CommandQueryResult>> {
      const saved = await load(requestId), snapshot = saved.command
      const checked = await lookup(snapshot, signal)
      // NoDurableResult is unresolved. Only this explicit retry call permits a POST.
      if (checked.result.kind !== 'noDurableResult' || saved.state === 'resolved') return checked
      const command = client.restore(snapshot)
      return record(snapshot, await client.execute(command, signal))
    },
    // The caller must first apply the durable result to its own basket/workspace state.
    async finalize(requestId: string) { await journal.finalize((await load(requestId)).command) },
  }
}
