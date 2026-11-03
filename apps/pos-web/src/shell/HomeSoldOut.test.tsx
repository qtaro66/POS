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

it('sets Sold Out and manually retries an uncertain response with the original operation ID', async () => {
  const user = userEvent.setup()
  const read = vi.fn<NonNullable<OrderingReadClient['soldOut']>>().mockResolvedValue({
    kind: 'ok', value: { totalCount: 0, nextOffset: null, items: [] },
  })
  const adjust = vi.fn<NonNullable<OrderingReadClient['adjustSoldOut']>>()
    .mockResolvedValueOnce('Unknown').mockResolvedValueOnce('Replayed')
  render(<HomeSoldOut read={read} adjust={adjust} />)
  await user.click(screen.getByRole('button', { name: 'ดูรายการ Sold Out ทั้งหมด' }))
  await user.type(screen.getByRole('textbox', { name: 'รหัสสินค้าที่จะตั้ง Sold Out' }), 'ITEM-01')
  await user.click(screen.getByRole('button', { name: 'ตั้ง Sold Out' }))
  const retry = await screen.findByRole('button', { name: 'ลองคำขอเดิมอีกครั้ง' })
  expect(adjust).toHaveBeenCalledTimes(1)
  const request = adjust.mock.calls[0][0]
  expect(request).toMatchObject({ sellableItemId: 'ITEM-01', expectedSoldOut: false, soldOut: true })
  await user.click(retry)
  await screen.findByText('ระบบเคยบันทึกคำขอนี้แล้ว')
  expect(adjust).toHaveBeenLastCalledWith(request, expect.any(AbortSignal))
})

it('clears a Sold Out item only after confirmation', async () => {
  const user = userEvent.setup()
  const read = vi.fn<NonNullable<OrderingReadClient['soldOut']>>().mockResolvedValue({
    kind: 'ok', value: { totalCount: 1, nextOffset: null, items: [{
      sellableItemId: 'ITEM-01', displayName: 'ข้าวผัด', stationId: null,
      changedByEmployeeId: 'EMP1', changedAt: '2026-11-03T00:00:00Z',
    }] },
  })
  const adjust = vi.fn<NonNullable<OrderingReadClient['adjustSoldOut']>>().mockResolvedValue('Applied')
  const view = render(<HomeSoldOut read={read} adjust={adjust} />)
  const dialog = view.container.querySelector('dialog')!
  dialog.showModal = () => { dialog.open = true }
  dialog.close = () => { dialog.open = false }
  await user.click(screen.getByRole('button', { name: 'ดูรายการ Sold Out ทั้งหมด' }))
  await user.click(await screen.findByRole('button', { name: 'ยกเลิก Sold Out ข้าวผัด' }))
  expect(adjust).not.toHaveBeenCalled()
  await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'กลับ' }))
  expect(adjust).not.toHaveBeenCalled()
  await user.click(screen.getByRole('button', { name: 'ยกเลิก Sold Out ข้าวผัด' }))
  await user.click(within(screen.getByRole('dialog')).getByRole('button', { name: 'ยืนยันยกเลิก Sold Out' }))
  await screen.findByText('บันทึก Sold Out แล้ว')
  expect(adjust).toHaveBeenCalledWith(expect.objectContaining({
    sellableItemId: 'ITEM-01', expectedSoldOut: true, soldOut: false,
  }), expect.any(AbortSignal))
})
