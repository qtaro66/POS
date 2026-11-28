import { act, fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { StrictMode } from 'react'
import { describe, expect, it, vi } from 'vitest'
import { KitchenReprintAction, KitchenReprintWorkspace, type KitchenReprintPort } from './KitchenReprintWorkspace.tsx'
import type { KitchenReprintReceipt, KitchenReprintResult, KitchenReprintStatus, KitchenReprintTarget } from '../api/kitchenReprintContracts.ts'
import type { KitchenReprintJournal, KitchenReprintRecoveryEntry } from '../ordering/kitchenReprintRecovery.ts'
import type { QueryResult } from '../api/orderingClient.ts'

const scope = { serviceOrigin: 'https://branch.invalid', branchId: 'branch', deviceId: 'device', employeeId: 'employee' }
const snapshot = { snapshotId: 'source', orderBatchId: 'batch', submittedRequestId: 'submit', tableSessionId: 'session', tableId: 'ORIGINAL', productionStationId: 'station', submittedAt: '2026-11-28T01:00:00Z', lineCount: 1, orderLineIds: ['line'] }
const line = { orderLineId: 'line', lineSequence: 1, displayName: 'Original item', quantity: 3, fulfillmentType: 'Takeaway' as const, comment: 'Original note', options: [{ optionGroupId: null, displayName: 'Original option', quantityPerItem: 2 }] }
const job = { printJobId: 'original', requestId: 'submit', snapshotId: 'source', productionStationId: 'station', status: 'Unknown' as const, attemptCount: 1, createdAt: snapshot.submittedAt, isReprint: false, originalPrintJobId: null }
const target: KitchenReprintTarget = { cashRequestId: 'cash', snapshotId: 'source', orderLineId: 'line', originalPrintJobId: 'original', tableSessionId: 'session', orderBatchId: 'batch', productionStationId: 'station' }
const commandId = '12345678-1234-1234-1234-123456789abc'
function receipt(id = commandId): KitchenReprintReceipt { return { schemaVersion: 1, branchId: 'branch', requestId: id, cashRequestId: 'cash', tableSessionId: 'session', orderBatchId: 'batch', orderLineId: 'line', originalSnapshotId: 'source', originalPrintJobId: 'original', reprintSnapshotId: `branch:${id}:kitchen-reprint-snapshot:1`, reprintPrintJobId: `branch:${id}:kitchen-reprint-print:1`, productionStationId: 'station', queuedAt: '2026-11-28T01:10:00Z' } }
function status(id = commandId, state: KitchenReprintStatus['status'] = 'Unknown'): QueryResult<KitchenReprintStatus> { return { kind: 'ok', value: { schemaVersion: 1, branchId: 'branch', asOf: '2026-11-28T01:11:00Z', receipt: receipt(id), status: state, attemptCount: 1 } } }
function port(): KitchenReprintPort {
  return { kitchenReprintScope: scope, kitchenReprint: vi.fn<KitchenReprintPort['kitchenReprint']>(async (_target, id) => ({ kind: 'queued', receipt: receipt(id) })), kitchenReprintStatus: vi.fn(async id => status(id)) }
}
function journal(initial: KitchenReprintRecoveryEntry | null = null) {
  let pending = initial
  const value: KitchenReprintJournal = {
    load: vi.fn(async () => pending),
    add: vi.fn(async command => { if (pending) throw new Error('Already pending'); pending = { formatVersion: 1, scope, ...command } }),
    clear: vi.fn(async id => { if (pending?.commandRequestId !== id) throw new Error('Wrong owner'); pending = null }),
  }
  return value
}
const action = <KitchenReprintAction requestId="cash" snapshot={snapshot} line={line} job={job} />
const open = () => screen.getByRole('button', { name: '🔒 พิมพ์ครัวซ้ำรายการนี้' })
const pin = () => screen.getByLabelText('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ') as HTMLInputElement
async function confirm(value = '111111') {
  await waitFor(() => expect((open() as HTMLButtonElement).disabled).toBe(false))
  await userEvent.click(open()); await userEvent.type(screen.getByLabelText('เหตุผลพิมพ์ครัวซ้ำ'), 'Operator requested copy')
  await userEvent.type(pin(), value)
}

describe('single-line kitchen copy workspace', () => {
  it('confirms the frozen original line and saves a PIN-free record before one double-click-safe POST', async () => {
    const client = port(), store = journal(), events: string[] = []
    const add = store.add; store.add = vi.fn(async entry => { events.push('save'); await add(entry) })
    let finish!: () => void
    client.kitchenReprint = vi.fn<KitchenReprintPort['kitchenReprint']>((_target, id, reason, entered) => { events.push('send'); expect(reason).toBe('Operator requested copy'); expect(entered).toBe('111111'); return new Promise<KitchenReprintResult>(resolve => { finish = () => resolve({ kind: 'queued', receipt: receipt(id) }) }) })
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm()
    const form = screen.getByRole('form', { name: 'ยืนยันสำเนาครัวทีละรายการ' })
    expect(form.textContent).toContain('จำนวนตอนส่ง 3'); expect(form.textContent).toContain('โต๊ะตอนส่ง ORIGINAL'); expect(form.textContent).toContain('สถานีเดิม station'); expect(form.textContent).toContain('Original option ×2')
    fireEvent.submit(form); fireEvent.submit(form)
    await waitFor(() => expect(client.kitchenReprint).toHaveBeenCalledTimes(1)); expect(pin().value).toBe('')
    expect(events).toEqual(['save', 'send']); expect(JSON.stringify(vi.mocked(store.add).mock.calls)).not.toContain('111111')
    await act(async () => finish())
    await screen.findByText('สาขารับคำขอพิมพ์สำเนาแล้ว')
    expect(await store.load()).toBeNull(); expect(store.clear).toHaveBeenCalledTimes(1)
    await userEvent.click(screen.getByRole('button', { name: 'ตรวจสถานะพิมพ์ครัวซ้ำ' }))
    await screen.findByText(/ไม่ทราบผล อาจมีกระดาษออกแล้ว/)
    expect(store.clear).toHaveBeenCalledTimes(1)
  })

  it('clears the PIN on rejection and requires a new PIN and new command on explicit retry', async () => {
    const client = port(), store = journal()
    vi.mocked(client.kitchenReprint).mockResolvedValueOnce({ kind: 'rejected', httpStatus: 401 })
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm(); await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
    await screen.findByText('PIN ไม่ผ่าน กรุณายืนยันใหม่'); expect(pin().value).toBe('')
    const reason = screen.getByRole('textbox', { name: 'เหตุผลพิมพ์ครัวซ้ำ' }) as HTMLTextAreaElement
    expect(reason.value).toBe('Operator requested copy')
    expect(document.querySelector(`label[for="${reason.id}"]`)?.textContent).toBe('เหตุผลพิมพ์ครัวซ้ำ')
    await userEvent.clear(reason); await userEvent.type(reason, 'Operator retried after PIN rejection')
    expect(screen.getByRole('textbox', { name: 'เหตุผลพิมพ์ครัวซ้ำ' })).toBe(reason)
    expect((screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }) as HTMLButtonElement).disabled).toBe(true)
    await userEvent.type(pin(), '222222'); await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
    await screen.findByText('สาขารับคำขอพิมพ์สำเนาแล้ว')
    const calls = vi.mocked(client.kitchenReprint).mock.calls
    expect(calls).toHaveLength(2); expect(calls[0][1]).not.toBe(calls[1][1]); expect(calls[1][3]).toBe('222222')
  })

  it('recovers a lost response with GET only after reload and displays Unknown without another POST', async () => {
    const client = port(), store = journal()
    vi.mocked(client.kitchenReprint).mockResolvedValue({ kind: 'uncertain', reason: 'network' })
    const view = render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm(); await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
    await screen.findByText('ยังยืนยันผลคำขอพิมพ์ซ้ำไม่ได้ ห้ามสั่งซ้ำ'); expect((open() as HTMLButtonElement).disabled).toBe(true)
    const saved = await store.load(); expect(saved).not.toBeNull(); expect(JSON.stringify(saved)).not.toContain('111111'); expect(JSON.stringify(saved)).not.toContain('Operator requested copy')
    view.unmount()
    render(<KitchenReprintWorkspace port={client} journal={store}><p>History not selected</p></KitchenReprintWorkspace>)
    await screen.findByText(/ไม่ทราบผล อาจมีกระดาษออกแล้ว/)
    expect(client.kitchenReprint).toHaveBeenCalledTimes(1); expect(client.kitchenReprintStatus).toHaveBeenCalledTimes(1)
    expect(screen.queryByLabelText('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ')).toBeNull()
  })

  it.each(['notFound', 'foreign-target', 'network'] as const)('retains a pending request when status is %s and never resubmits', async fault => {
    const client = port(), store = journal({ formatVersion: 1, scope, commandRequestId: commandId, target })
    vi.mocked(client.kitchenReprintStatus).mockResolvedValue(fault === 'notFound' ? { kind: 'notFound' } : fault === 'network' ? { kind: 'unavailable', reason: 'network' } : { ...status(), value: { ...(status() as Extract<ReturnType<typeof status>, { kind: 'ok' }>).value, receipt: { ...receipt(), orderLineId: 'OTHER' } } } as QueryResult<KitchenReprintStatus>)
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await screen.findByText('ยังยืนยันผลคำขอพิมพ์ซ้ำไม่ได้ ห้ามสั่งซ้ำ')
    expect((open() as HTMLButtonElement).disabled).toBe(true); expect(store.clear).not.toHaveBeenCalled(); expect(client.kitchenReprint).not.toHaveBeenCalled()
    await userEvent.click(screen.getByRole('button', { name: 'ตรวจสถานะพิมพ์ครัวซ้ำ' })); await waitFor(() => expect(client.kitchenReprintStatus).toHaveBeenCalledTimes(2)); expect(await store.load()).not.toBeNull()
  })

  it('blocks sending if the recovery transaction fails and clears the entered PIN', async () => {
    const client = port(), store = journal(); vi.mocked(store.add).mockRejectedValue(new Error('Storage disabled'))
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm(); await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
    await screen.findByText('ยังบันทึกข้อมูลกู้คำขอในเครื่องไม่ได้ จึงยังไม่ได้สั่งพิมพ์ซ้ำ')
    expect(pin().value).toBe(''); expect(client.kitchenReprint).not.toHaveBeenCalled()
  })

  it('clears confirmation on target navigation and ignores an old owner response', async () => {
    const client = port(), store = journal(); let finish!: (result: KitchenReprintResult) => void
    vi.mocked(client.kitchenReprint).mockImplementation(() => new Promise(resolve => { finish = resolve }))
    const view = render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm(); await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
    await waitFor(() => expect(client.kitchenReprint).toHaveBeenCalledOnce())
    view.rerender(<KitchenReprintWorkspace port={client} journal={store}><p>Another selection</p></KitchenReprintWorkspace>)
    expect(screen.queryByLabelText('PIN ผู้มีสิทธิ์พิมพ์ครัวซ้ำ')).toBeNull()
    const replacement = { ...port(), kitchenReprintScope: { ...scope, employeeId: 'OTHER' } }
    view.rerender(<KitchenReprintWorkspace port={replacement} journal={journal()}>{action}</KitchenReprintWorkspace>)
    await act(async () => finish({ kind: 'queued', receipt: receipt() }))
    expect(screen.queryByText('สาขารับคำขอพิมพ์สำเนาแล้ว')).toBeNull(); expect(await store.load()).not.toBeNull()
  })

  it('closing confirmation discards the PIN and legacy read-only ports expose no action', async () => {
    const client = port(), store = journal()
    const view = render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm(); await userEvent.click(screen.getByRole('button', { name: 'ปิดการยืนยันพิมพ์ซ้ำ' }))
    await userEvent.click(open()); expect(pin().value).toBe('')
    view.rerender(<KitchenReprintWorkspace>{action}</KitchenReprintWorkspace>)
    expect(screen.queryByRole('button', { name: '🔒 พิมพ์ครัวซ้ำรายการนี้' })).toBeNull(); expect(client.kitchenReprint).not.toHaveBeenCalled()
  })

  it('ignores a delayed initial journal read after the journal owner is replaced', async () => {
    const client = port(), oldStore = journal(); let finish!: (entry: KitchenReprintRecoveryEntry | null) => void
    vi.mocked(oldStore.load).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    const view = render(<KitchenReprintWorkspace port={client} journal={oldStore}>{action}</KitchenReprintWorkspace>)
    view.rerender(<KitchenReprintWorkspace port={client} journal={journal()}>{action}</KitchenReprintWorkspace>)
    await waitFor(() => expect((open() as HTMLButtonElement).disabled).toBe(false))
    await act(async () => finish({ formatVersion: 1, scope, commandRequestId: commandId, target }))
    expect(client.kitchenReprintStatus).not.toHaveBeenCalled(); expect(screen.queryByText(/มีคำขอพิมพ์ซ้ำรอตรวจผล/)).toBeNull()
  })

  it('ignores the first delayed effect read when StrictMode restarts the effect', async () => {
    const client = port(), store = journal(); let finish!: (entry: KitchenReprintRecoveryEntry | null) => void
    vi.mocked(store.load).mockImplementationOnce(() => new Promise(resolve => { finish = resolve }))
    render(<StrictMode><KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace></StrictMode>)
    await waitFor(() => expect((open() as HTMLButtonElement).disabled).toBe(false))
    await act(async () => finish({ formatVersion: 1, scope, commandRequestId: commandId, target }))
    expect(client.kitchenReprintStatus).not.toHaveBeenCalled()
  })

  it('allows a fresh action after a transient journal clear failure is resolved by another status read', async () => {
    const client = port(), store = journal({ formatVersion: 1, scope, commandRequestId: commandId, target })
    vi.mocked(store.clear).mockRejectedValueOnce(new Error('Transient storage failure'))
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await screen.findByText('ยังปรับข้อมูลกู้สถานะในเครื่องไม่ได้ กรุณาตรวจสถานะเดิมก่อนทำรายการใหม่')
    expect((open() as HTMLButtonElement).disabled).toBe(true)
    await userEvent.click(screen.getByRole('button', { name: 'ตรวจสถานะพิมพ์ครัวซ้ำ' }))
    await userEvent.click(await screen.findByRole('button', { name: 'ปิดสถานะสำเนา' }))
    await waitFor(() => expect((open() as HTMLButtonElement).disabled).toBe(false))
    expect(await store.load()).toBeNull(); expect(client.kitchenReprint).not.toHaveBeenCalled()
  })

  it('does not send if secure command ID generation is unavailable', async () => {
    const client = port(), store = journal()
    render(<KitchenReprintWorkspace port={client} journal={store}>{action}</KitchenReprintWorkspace>)
    await confirm()
    const random = vi.spyOn(crypto, 'randomUUID').mockImplementation(() => { throw new Error('Unavailable') })
    try {
      await userEvent.click(screen.getByRole('button', { name: 'ยืนยันพิมพ์สำเนารายการนี้' }))
      await screen.findByText('ยังบันทึกข้อมูลกู้คำขอในเครื่องไม่ได้ จึงยังไม่ได้สั่งพิมพ์ซ้ำ')
      expect(pin().value).toBe(''); expect(client.kitchenReprint).not.toHaveBeenCalled(); expect(store.add).not.toHaveBeenCalled()
    } finally { random.mockRestore() }
  })
})
