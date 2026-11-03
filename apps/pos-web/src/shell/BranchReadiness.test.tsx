import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { BranchReadinessPanel } from './BranchReadinessPanel.tsx'

it('shows a negative database check separately from service availability and clears it on refresh failure', async () => {
  const read = vi.fn()
    .mockResolvedValueOnce({ kind: 'ok', value: { checkedAt: '2026-11-03T00:00:00Z', orderingDatabaseReady: false } })
    .mockResolvedValueOnce({ kind: 'unavailable', reason: 'network' })
  render(<BranchReadinessPanel read={read} />)
  await screen.findByText('ตรวจความพร้อมไม่ผ่าน')
  expect(screen.getByText('ตอบกลับการตรวจสอบ')).toBeTruthy()
  await userEvent.click(screen.getByRole('button', { name: 'ตรวจสอบ Branch ใหม่' }))
  await screen.findByText('อ่านสถานะไม่ได้')
  expect(screen.queryByText('ตรวจความพร้อมไม่ผ่าน')).toBeNull()
})
