import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import { HomeCancellationQueue } from './HomeCancellationQueue.tsx'
import type { OrderingReadClient } from '../ordering/readController.ts'

it('shows requested quantities and clears resolved requests on refresh', async () => {
  const read = vi.fn<NonNullable<OrderingReadClient['cancellationQueue']>>()
    .mockResolvedValueOnce({ kind: 'ok', value: [{
      cancellationRequestId: 'C1', tableSessionId: 'T1', status: 'CancellationRequested',
      lines: [{ orderLineId: 'L1', displayName: 'ข้าวผัด', requestedQuantity: 2 }],
    }] })
    .mockResolvedValueOnce({ kind: 'ok', value: [] })
  render(<HomeCancellationQueue read={read} />)
  expect(await screen.findByText('ข้าวผัด × 2')).toBeTruthy()
  await userEvent.setup().click(screen.getByRole('button', { name: 'อ่านคิวใหม่' }))
  expect(await screen.findByText('ไม่มีคำขอค้าง')).toBeTruthy()
  expect(screen.queryByText('ข้าวผัด × 2')).toBeNull()
})
