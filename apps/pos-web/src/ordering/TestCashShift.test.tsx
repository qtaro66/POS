import { act, fireEvent, render, screen, within } from '@testing-library/react'
import { afterEach, expect, it, vi } from 'vitest'
import { TestCashShift } from './TestCashShift.tsx'

afterEach(() => localStorage.clear())

it('reads only the stored shift for the selected branch', async () => {
  localStorage.setItem('branch-a', 'shift-a')
  localStorage.setItem('branch-b', 'shift-b')
  const status = vi.fn(async () => ({ status: 200, body: { activeShiftId: null, openingCash: null, close: null } }))
  const run = vi.fn()
  const view = render(<TestCashShift key="a" storageKey="branch-a" run={run} status={status} />)
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบสถานะกะ' }))
  await screen.findByText('ไม่มีกะเปิดอยู่')
  expect(status).toHaveBeenLastCalledWith('shift-a')
  view.rerender(<TestCashShift key="b" storageKey="branch-b" run={run} status={status} />)
  fireEvent.click(screen.getByRole('button', { name: 'ตรวจสอบสถานะกะ' }))
  await screen.findByText('ไม่มีกะเปิดอยู่')
  expect(status).toHaveBeenLastCalledWith('shift-b')
  expect(run).not.toHaveBeenCalled()
})

it.each(['open', 'close'] as const)('serializes a delayed status read against %s and repeat reads', async operation => {
  localStorage.setItem('branch-a', 'shift-a')
  let finish!: (result: { status: number; body: unknown }) => void
  const status = vi.fn(() => new Promise<{ status: number; body: unknown }>(resolve => { finish = resolve }))
  const run = vi.fn(async () => ({ status: 200, body: { expectedCash: 500, countedCash: 500, difference: 0 } }))
  render(<TestCashShift storageKey="branch-a" run={run} status={status} />)
  const check = screen.getByRole('button', { name: 'ตรวจสอบสถานะกะ' })
  const form = check.closest('form')!
  const fields = within(form)
  fireEvent.click(check)
  fireEvent.click(check)
  fireEvent.change(fields.getByRole('combobox'), { target: { value: operation } })
  fireEvent.change(fields.getByRole('spinbutton'), { target: { value: '500' } })
  fireEvent.change(fields.getByLabelText('ยืนยัน PIN'), { target: { value: '123456' } })
  fireEvent.submit(form)
  expect(status).toHaveBeenCalledTimes(1)
  expect(run).not.toHaveBeenCalled()
  await act(async () => finish({ status: 200, body: { activeShiftId: 'shift-a', openingCash: 500, close: null } }))
  fireEvent.submit(form)
  expect(run).toHaveBeenCalledWith(operation, 'shift-a', 500, '123456')
  await screen.findByText(operation === 'open' ? 'เปิดกะทดสอบแล้ว' : /ปิดกะแล้ว/)
})
