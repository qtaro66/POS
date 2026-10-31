import { StrictMode } from 'react'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { IDBFactory } from 'fake-indexeddb'
import App from '../App.tsx'
import { createOrderingClient } from '../api/orderingClient.ts'
import { createCommandJournal } from './commandJournal.ts'
import { CommandRecoveryPanel } from './CommandRecoveryPanel.tsx'
import { recoveryStrings as s } from './recoveryStrings.ts'
import type { RecoveryReadPort } from './recoveryController.ts'
import type { JournalEntry } from './commandJournal.ts'
import { createPreviewRecoverySession } from './preview.ts'

function fixture() {
  const fetch = vi.fn(async (_url: string, _init: RequestInit) => new Response(JSON.stringify({ schemaVersion: 1, includesAvailableTables: false, sessions: [] }), { headers: { 'Content-Type': 'application/json' } }))
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example.test', branchId: 'BRANCH-01', deviceId: 'DEVICE-01', employeeId: 'EMP-01', deviceCredential: 'v1.aaaa', employeeSessionToken: 'v1.bbbb' }, { fetch, requestId: () => 'REQUEST-01', now: () => new Date('2026-10-31T10:00:00Z') })
  const entry: JournalEntry = { command: client.snapshot(client.prepareSubmitOrder('SESSION-01', [{ sellableItemId: 'F001', quantity: 2, fulfillmentType: 'Takeaway', comment: 'ไม่เผ็ด', options: [{ optionGroupId: 'TOP', sellableItemId: 'EGG', quantity: 1 }] }])), state: 'pending' }
  return { client, fetch, entry }
}
const missing = { result: { kind: 'noDurableResult' as const, requestId: 'REQUEST-01' }, recoveryRecorded: true }
function durable(status: 'BranchAccepted' | 'Rejected' = 'BranchAccepted', recoveryRecorded = true) {
  return { result: { kind: 'durable' as const, response: { result: { schemaVersion: 1 as const, requestId: 'REQUEST-01', status, tableSessionId: 'SESSION-01', tableSessionStatus: 'Ordering' as const, tableVersion: 1, errorCode: status === 'Rejected' ? 'SoldOut' : null, serverReceivedAt: '2026-10-31T10:01:00Z', branchSequence: 1 }, validationDetails: null } }, recoveryRecorded }
}
async function open() { const user = userEvent.setup(); await user.click(screen.getByRole('button', { name: s.title })); return user }

describe('PC POS command recovery inspection', () => {
  it('runs the synthetic preview with accepted and unresolved requests without external fetch', async () => {
    const external = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('External fetch forbidden'))
    try {
      render(<App {...createPreviewRecoverySession()} preview />)
      const user = await open()
      const accepted = await screen.findByRole('article', { name: 'คำขอ PREVIEW-RECOVERY-REQUEST-1' })
      await user.click(within(accepted).getByRole('button', { name: s.check }))
      expect(await screen.findByText(s.accepted)).toBeTruthy()
      const unresolved = screen.getByRole('article', { name: 'คำขอ PREVIEW-RECOVERY-REQUEST-2' })
      await user.click(within(unresolved).getByRole('button', { name: s.check }))
      expect(await screen.findByText(s.missing)).toBeTruthy()
      expect(external).not.toHaveBeenCalled()
    } finally { external.mockRestore() }
  })
  it('lists original IDs, quantities, fulfillment, options and comments without a status call', async () => {
    const { entry } = fixture(), recover = vi.fn(async () => missing)
    render(<CommandRecoveryPanel port={{ list: async () => [entry], recover }} />)
    await open()
    expect(await screen.findByRole('article', { name: 'คำขอ REQUEST-01' })).toBeTruthy()
    expect(screen.getByText('EMP-01')).toBeTruthy()
    expect(screen.getByText(/F001 × 2 · กลับบ้าน/)).toBeTruthy()
    expect(screen.getByText(/EGG × 1/)).toBeTruthy()
    expect(screen.getByText(/ไม่เผ็ด/)).toBeTruthy()
    expect(screen.getByText(s.pending)).toBeTruthy()
    expect(recover).not.toHaveBeenCalled()
    expect(screen.queryByRole('button', { name: /ส่งซ้ำ|ลบคำขอ|ส่งออเดอร์|พิมพ์/ })).toBeNull()
  })

  it('does not display a local resolved marker as fresh Branch acceptance', async () => {
    const { entry } = fixture()
    render(<CommandRecoveryPanel port={{ list: async () => [{ ...entry, state: 'resolved' }], recover: async () => missing }} />)
    await open()
    expect(await screen.findByText(s.resolved)).toBeTruthy()
    expect(screen.queryByText(s.accepted)).toBeNull()
    await userEvent.click(screen.getByRole('button', { name: s.check }))
    expect(await screen.findByText(s.missing)).toBeTruthy()
    expect(screen.queryByText(s.rejected)).toBeNull()
  })

  for (const status of ['BranchAccepted', 'Rejected'] as const) {
    it(`shows the fresh ${status} receipt and keeps its entry for later basket reconciliation`, async () => {
      const { entry } = fixture()
      render(<CommandRecoveryPanel port={{ list: async () => [entry], recover: async () => durable(status, false) }} />)
      const user = await open()
      await user.click(await screen.findByRole('button', { name: s.check }))
      expect(await screen.findByText(status === 'BranchAccepted' ? s.accepted : s.rejected)).toBeTruthy()
      expect(screen.getByText(s.localWarning)).toBeTruthy()
      expect(screen.getByRole('article', { name: 'คำขอ REQUEST-01' })).toBeTruthy()
      if (status === 'Rejected') expect(screen.getByText(/SoldOut/)).toBeTruthy()
    })
  }

  it('distinguishes empty storage from a storage failure without leaking error details', async () => {
    const { rerender } = render(<CommandRecoveryPanel port={{ list: async () => [], recover: async () => missing }} />)
    await open()
    expect(await screen.findByText(s.empty)).toBeTruthy()
    rerender(<CommandRecoveryPanel port={{ list: async () => { throw new Error('Private diagnostic') }, recover: async () => missing }} />)
    expect(screen.queryByText(s.empty)).toBeNull()
    expect(await screen.findByRole('alert')).toHaveProperty('textContent', s.storageError)
    expect(screen.queryByText(/Private diagnostic/)).toBeNull()
  })

  it('supports Escape/close focus return, cancels pending reads, and ignores late results', async () => {
    const { entry } = fixture()
    let resolve!: (value: ReturnType<typeof durable>) => void
    let signal: AbortSignal | undefined
    const port: RecoveryReadPort = { list: async () => [entry], recover: async (_id, passed) => { signal = passed; return new Promise(done => { resolve = done }) } }
    render(<CommandRecoveryPanel port={port} />)
    const user = await open()
    await user.click(await screen.findByRole('button', { name: s.check }))
    await user.keyboard('{Escape}')
    expect(signal?.aborted).toBe(true)
    expect(screen.queryByRole('region', { name: s.title })).toBeNull()
    expect(document.activeElement).toBe(screen.getByRole('button', { name: s.title }))
    await act(async () => { resolve(durable()) })
    await user.click(screen.getByRole('button', { name: s.title }))
    expect(await screen.findByText(s.pending)).toBeTruthy()
    expect(screen.queryByText(s.accepted)).toBeNull()
    await user.click(screen.getByRole('button', { name: s.close }))
    expect(document.activeElement).toBe(screen.getByRole('button', { name: s.title }))
  })

  it('replaces identity-scoped content synchronously when its port changes', async () => {
    const { entry } = fixture()
    const { rerender } = render(<CommandRecoveryPanel port={{ list: async () => [entry], recover: async () => missing }} />)
    await open(); await screen.findByText('EMP-01')
    rerender(<CommandRecoveryPanel port={{ list: () => new Promise(() => {}), recover: async () => missing }} />)
    expect(screen.queryByText('EMP-01')).toBeNull()
    expect(screen.queryByText('REQUEST-01')).toBeNull()
  })

  it('uses the authenticated App client and real journal, GET only, with lock hiding all recovery data', async () => {
    const { client, fetch, entry } = fixture(), journal = createCommandJournal(new IDBFactory())
    await journal.add(entry.command)
    const { rerender } = render(<StrictMode><App client={client} journal={journal} /></StrictMode>)
    const user = await open()
    const article = await screen.findByRole('article', { name: 'คำขอ REQUEST-01' })
    await user.click(within(article).getByRole('button', { name: s.check }))
    expect(await screen.findByText(s.unavailable)).toBeTruthy()
    expect(fetch.mock.calls.every(call => call[1]?.method === 'GET')).toBe(true)
    await user.click(screen.getByRole('button', { name: 'ล็อกหน้าจอ' }))
    expect(screen.queryByText('EMP-01')).toBeNull()
    expect(screen.queryByRole('button', { name: s.title })).toBeNull()
    rerender(<App client={client} journal={journal} />)
    expect(screen.queryByText('REQUEST-01')).toBeNull()
  })
})
