import { act, renderHook, waitFor } from '@testing-library/react'
import { expect, it, vi } from 'vitest'
import type { ActiveTablesGateway, ActiveTablesOutcome, MenuGateway, MenuOutcome } from '../adapters/ports.ts'
import { useActiveTables } from './useActiveTables.ts'
import { useMenu } from './useMenu.ts'

const device = { deviceId: 'D1', deviceName: 'Demo', branchId: 'B1', branchName: 'Demo',
  branchServiceUrl: 'https://branch.test', pairedAt: '2026-11-01T00:00:00Z' }
const employee = { employeeId: 'E1', displayName: 'Demo', authenticatedAt: device.pairedAt }
const other = { ...employee, employeeId: 'E2' }

function deferred<T>() {
  let resolve!: (value: T) => void
  const promise = new Promise<T>(done => { resolve = done })
  return { promise, resolve }
}

it('cancels superseded table reads and retains the last list only for the current login', async () => {
  const old = deferred<ActiveTablesOutcome>(), recent = deferred<ActiveTablesOutcome>()
  const current: ActiveTablesOutcome = { kind: 'ok', snapshot: { tables: [], receivedAt: device.pairedAt } }
  const list = vi.fn<ActiveTablesGateway['listActiveTables']>().mockReturnValueOnce(old.promise)
    .mockReturnValueOnce(recent.promise).mockResolvedValue({ kind: 'unreachable' })
  const gateway = { listActiveTables: list }
  const view = renderHook(({ user }) => useActiveTables(device, gateway, user), { initialProps: { user: employee } })
  act(() => view.result.current.refresh())
  expect(list.mock.calls[0][2]?.aborted).toBe(true)
  await act(() => recent.resolve(current))
  await act(() => old.resolve({ kind: 'unreachable' }))
  expect(view.result.current.snapshot).toBe(current.snapshot)
  expect(view.result.current.problem).toBeNull()
  act(() => view.result.current.refresh())
  await waitFor(() => expect(view.result.current.problem).toBe('unreachable'))
  expect(view.result.current.snapshot).toBe(current.snapshot)
  view.rerender({ user: other })
  expect(view.result.current.snapshot).toBeNull()
})

it('clears the previous menu and aborts pending work when the employee or table changes', async () => {
  const old = deferred<MenuOutcome>()
  const menu = { masterDataVersion: 'V1', categories: [], items: [] }
  const load = vi.fn<MenuGateway['loadMenu']>().mockResolvedValueOnce({ kind: 'ok', menu })
    .mockReturnValueOnce(old.promise).mockResolvedValue({ kind: 'unreachable' })
  const gateway = { loadMenu: load }
  const view = renderHook(({ user, session }) => useMenu(gateway, device, session, user),
    { initialProps: { user: employee, session: 'TABLE-01' } })
  await waitFor(() => expect(view.result.current.state.status).toBe('ready'))
  view.rerender({ user: other, session: 'TABLE-01' })
  expect(view.result.current.state.status).toBe('loading')
  view.rerender({ user: other, session: 'TABLE-02' })
  expect(load.mock.calls[1][3]?.aborted).toBe(true)
  await act(() => old.resolve({ kind: 'ok', menu }))
  await waitFor(() => expect(view.result.current.state.status).toBe('unreachable'))
})
