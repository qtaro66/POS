import { StrictMode } from 'react'
import { act, render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { describe, expect, it, vi } from 'vitest'
import { BasketPanel } from './BasketPanel.tsx'
import { createBasketPreview } from './basketPreview.ts'
import { basketPanelStrings as s, editorIssueLabels } from './basketPanelStrings.ts'
import type { BasketEditorState } from './basketEditor.ts'

describe('persistent PC basket panel', () => {
  it('shows committed mixed-fulfillment lines, options and unit prices without invented bill totals', async () => {
    const fixture = await createBasketPreview()
    render(<StrictMode><BasketPanel controller={fixture.controller} /></StrictMode>)
    expect(await screen.findByRole('article', { name: /ข้าวผัดกุ้ง/ })).toBeTruthy()
    expect(screen.getByRole('region', { name: s.dineIn })).toBeTruthy()
    expect(screen.getByRole('region', { name: s.takeaway })).toBeTruthy()
    expect(screen.getByText(/ไข่ดาว ×1/)).toBeTruthy()
    expect(screen.getByText(/ไม่ใส่ต้นหอม/)).toBeTruthy()
    expect(screen.getByText(s.confirmedTotals)).toBeTruthy()
    expect(screen.queryByText(/ยอดสุทธิ|รวมทั้งสิ้น|บันทึก Draft|บันทึกตะกร้า/)).toBeNull()
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('quantity and per-line fulfillment edits persist and preserve keyboard focus', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    render(<BasketPanel controller={fixture.controller} />)
    const plus = await screen.findByRole('button', { name: 'เพิ่มจำนวน ข้าวผัดกุ้ง' })
    await user.click(plus)
    await waitFor(() => { expect(screen.getByLabelText('จำนวน ข้าวผัดกุ้ง').textContent).toBe('2') })
    expect(document.activeElement).toBe(plus)
    await user.selectOptions(screen.getByRole('combobox', { name: 'รับสินค้า ข้าวผัดกุ้ง' }), 'Takeaway')
    await waitFor(() => { expect((screen.getByRole('combobox', { name: 'รับสินค้า ข้าวผัดกุ้ง' }) as HTMLSelectElement).value).toBe('Takeaway') })
    const saved = await fixture.baskets.load(fixture.scope)
    expect(saved?.lines[0].quantity).toBe(2)
    expect(saved?.lines[0].fulfillmentType).toBe('Takeaway')
    expect(fixture.state.postedBodies).toHaveLength(0)
  })

  it('new fulfillment changes do not rewrite existing lines', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    render(<BasketPanel controller={fixture.controller} />)
    await screen.findByRole('article', { name: /ข้าวผัดกุ้ง/ })
    await user.click(screen.getByRole('radio', { name: s.takeaway }))
    expect(fixture.controller.getSnapshot().newFulfillment).toBe('Takeaway')
    expect((await fixture.baskets.load(fixture.scope))?.lines.map(line => line.fulfillmentType)).toEqual(['DineIn', 'Takeaway'])
  })

  it('comment editing and removal operate on the committed draft only', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    render(<BasketPanel controller={fixture.controller} />)
    const article = await screen.findByRole('article', { name: /ข้าวผัดกุ้ง/ })
    await user.click(within(article).getByText(s.comment))
    const field = within(article).getByRole('textbox', { name: 'หมายเหตุ' })
    await user.clear(field); await user.type(field, 'ไม่เผ็ด')
    expect((await fixture.baskets.load(fixture.scope))?.lines[0].comment).toBe('ไม่ใส่ต้นหอม')
    await user.click(within(article).getByRole('button', { name: s.applyComment }))
    await waitFor(() => { expect(screen.getByText('หมายเหตุ: ไม่เผ็ด')).toBeTruthy() })
    expect(document.activeElement?.textContent).toBe(s.applyComment)
    await user.click(screen.getByRole('button', { name: `${s.remove} น้ำมะนาวโซดา` }))
    await waitFor(() => { expect(screen.queryByRole('article', { name: /น้ำมะนาวโซดา/ })).toBeNull() })
    expect((await fixture.baskets.load(fixture.scope))?.lines).toHaveLength(1)
  })

  it('a locked table retains the basket but disables draft mutation and submission', async () => {
    const fixture = await createBasketPreview()
    fixture.state.locked = true
    render(<BasketPanel controller={fixture.controller} />)
    await screen.findByText(editorIssueLabels.sessionLocked)
    expect((screen.getByRole('button', { name: 'เพิ่มจำนวน ข้าวผัดกุ้ง' }) as HTMLButtonElement).disabled).toBe(true)
    expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 2 รายการ' }) as HTMLButtonElement).disabled).toBe(true)
    expect((await fixture.baskets.load(fixture.scope))?.lines).toHaveLength(2)
  })

  it('withholds names and prices from a mismatched menu version instead of repricing the draft', async () => {
    const fixture = await createBasketPreview()
    fixture.state.menuVersion = 'CHANGED-MASTER'
    render(<BasketPanel controller={fixture.controller} />)
    await screen.findByText(editorIssueLabels.catalogChanged)
    expect(screen.queryByText('ข้าวผัดกุ้ง')).toBeNull()
    expect(screen.queryByText(/185\.00/)).toBeNull()
    expect(screen.getByRole('article', { name: /PREVIEW-F001/ })).toBeTruthy()
    expect((await fixture.baskets.load(fixture.scope))?.sessionMasterDataVersion).toBe('PREVIEW-BASKET-MASTER')
  })

  it('accepted submission shows the real outcome, clears atomically and focuses the result', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    const external = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('External fetch forbidden'))
    try {
      render(<BasketPanel controller={fixture.controller} />)
      await user.click(await screen.findByRole('button', { name: 'ส่งออเดอร์ 2 รายการ' }))
      expect(await screen.findByText(s.accepted)).toBeTruthy()
      expect(document.activeElement?.textContent).toContain(s.accepted)
      expect(await fixture.baskets.load(fixture.scope)).toBeNull()
      expect(fixture.state.postedBodies).toHaveLength(1)
      expect(external).not.toHaveBeenCalled()
      expect((screen.getByRole('button', { name: 'ส่งออเดอร์ 0 รายการ' }) as HTMLButtonElement).disabled).toBe(true)
    } finally { external.mockRestore() }
  })

  it('rejection retains original lines for review instead of showing acceptance', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    fixture.state.postBehavior = 'reject'
    render(<BasketPanel controller={fixture.controller} />)
    await user.click(await screen.findByRole('button', { name: 'ส่งออเดอร์ 2 รายการ' }))
    expect(await screen.findByText(s.rejected)).toBeTruthy()
    expect(screen.queryByText(s.accepted)).toBeNull()
    expect((await fixture.baskets.load(fixture.scope))?.lines).toHaveLength(2)
    expect(fixture.state.postedBodies).toHaveLength(1)
  })

  it('lost acceptance stays pending until GET recovery, with no additional POST', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    fixture.state.postBehavior = 'lostAfterAccept'
    render(<BasketPanel controller={fixture.controller} />)
    await user.click(await screen.findByRole('button', { name: 'ส่งออเดอร์ 2 รายการ' }))
    const check = await screen.findByRole('button', { name: s.check })
    expect((screen.getByRole('button', { name: 'เพิ่มจำนวน ข้าวผัดกุ้ง' }) as HTMLButtonElement).disabled).toBe(true)
    await user.click(check)
    expect(await screen.findByText(s.accepted)).toBeTruthy()
    expect(fixture.state.postedBodies).toHaveLength(1)
    expect(await fixture.baskets.load(fixture.scope)).toBeNull()
  })

  it('a durable acceptance with failed local cleanup is never relabeled as an unknown Branch outcome', async () => {
    const fixture = await createBasketPreview()
    await fixture.controller.refresh()
    const snapshot: BasketEditorState = { ...fixture.controller.getSnapshot(), issue: 'refreshRequired', basket: { ...fixture.controller.getSnapshot().basket!, pendingRequestId: 'REQUEST-01' }, outcome: {
      kind: 'durable', requestId: 'REQUEST-01', localApplied: false, response: { result: { schemaVersion: 1, requestId: 'REQUEST-01', status: 'BranchAccepted', tableSessionId: fixture.scope.tableSessionId, tableSessionStatus: 'Ordering', tableVersion: 1, serverReceivedAt: '2026-11-01T04:00:00Z', branchSequence: 1, errorCode: null }, validationDetails: null },
    } }
    const controller = { ...fixture.controller, getSnapshot: () => snapshot, subscribe: () => () => {}, refresh: async () => {} }
    render(<BasketPanel controller={controller} />)
    expect(screen.getByText(s.accepted)).toBeTruthy()
    expect(screen.getByText(s.localWarning)).toBeTruthy()
    expect(screen.queryByText(s.pending)).toBeNull()
    expect(screen.getByRole('button', { name: s.check })).toBeTruthy()
  })

  it('retry is an explicit action and preserves original request bytes', async () => {
    const fixture = await createBasketPreview(), user = userEvent.setup()
    fixture.state.postBehavior = 'unavailable'
    render(<BasketPanel controller={fixture.controller} />)
    await user.click(await screen.findByRole('button', { name: 'ส่งออเดอร์ 2 รายการ' }))
    const retry = await screen.findByRole('button', { name: s.retry })
    expect(fixture.state.postedBodies).toHaveLength(1)
    fixture.state.postBehavior = 'accept'
    await user.click(retry)
    expect(await screen.findByText(s.accepted)).toBeTruthy()
    expect(fixture.state.postedBodies).toHaveLength(2)
    expect(fixture.state.postedBodies[1]).toBe(fixture.state.postedBodies[0])
  })

  it('switching controller clears previous identity content synchronously', async () => {
    const fixture = await createBasketPreview(), replacement = await createBasketPreview()
    const { rerender } = render(<BasketPanel controller={fixture.controller} />)
    await screen.findByRole('article', { name: /ข้าวผัดกุ้ง/ })
    replacement.state.failReads = true
    rerender(<BasketPanel controller={replacement.controller} />)
    expect(screen.queryByRole('article', { name: /ข้าวผัดกุ้ง/ })).toBeNull()
    await act(async () => {})
    await screen.findByText(editorIssueLabels.sessionUnavailable)
    expect(screen.queryByText('185.00 บาท')).toBeNull()
  })
})
