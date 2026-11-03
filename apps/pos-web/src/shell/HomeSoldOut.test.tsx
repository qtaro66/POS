import { render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import type { OrderingReadClient } from '../ordering/readController.ts'
import { HomeSoldOut } from './HomeSoldOut.tsx'

it('shows only six recent rows and searches the complete branch list', async () => {
  const user = userEvent.setup()
  const items = Array.from({ length: 7 }, (_, index) => ({
    sellableItemId: `I-${index}`, displayName: index === 6 ? 'ข้าวผัด' : null, stationId: null,
    changedByEmployeeId: 'EMP1', changedAt: '2026-11-03T00:00:00Z',
  }))
  const read = vi.fn<NonNullable<OrderingReadClient['soldOut']>>().mockImplementation(async search => ({
    kind: 'ok', value: { totalCount: search ? 1 : 7, nextOffset: null, items: search ? items.slice(6) : items },
  }))
  render(<HomeSoldOut read={read} />)
  await screen.findByText('ทั้งหมด 7 รายการ')
  expect(screen.getAllByRole('listitem')).toHaveLength(6)
  await user.click(screen.getByRole('button', { name: 'ดูรายการ Sold Out ทั้งหมด' }))
  await screen.findByText('ข้าวผัด')
  await user.type(screen.getByRole('textbox'), 'ข้าว')
  await user.click(screen.getByRole('button', { name: 'ค้นหา Sold Out' }))
  await screen.findByText('พบ 1 รายการ')
  expect(within(screen.getByRole('list')).getAllByRole('listitem')).toHaveLength(1)
  expect(read).toHaveBeenLastCalledWith('ข้าว', 0, expect.any(AbortSignal))
})
