import { render, screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { expect, it, vi } from 'vitest'
import type { SoldOutGateway } from '../adapters/ports.ts'
import { MobileSoldOut } from './MobileSoldOut.tsx'

const device = { deviceId: 'D1', deviceName: 'Demo', branchId: 'B1', branchName: 'Demo',
  branchServiceUrl: 'https://branch.test', pairedAt: '2026-11-01T00:00:00Z' }
const employee = { employeeId: 'E1', displayName: 'Demo', authenticatedAt: '2026-11-01T00:00:00Z' }
const items = [{ sellableItemId: 'I1', displayName: 'Fried rice' }]

it('shows the list while capability is denied and exposes no write controls', async () => {
  const adjust = vi.fn<SoldOutGateway['adjust']>()
  render(<MobileSoldOut device={device} employee={employee} gateway={{
    adjust, read: async () => ({ items, canSetSoldOut: false, nextOffset: null }),
  }} />)
  await screen.findByText('Fried rice')
  expect(screen.getByText('View only')).toBeTruthy()
  expect(screen.queryByRole('button', { name: 'Set Sold Out' })).toBeNull()
  expect(screen.queryByRole('button', { name: 'Clear Sold Out for Fried rice' })).toBeNull()
  expect(adjust).not.toHaveBeenCalled()
})

it('sets by item ID and retries uncertainty with the same operation', async () => {
  const user = userEvent.setup()
  const adjust = vi.fn<SoldOutGateway['adjust']>().mockResolvedValueOnce('unknown').mockResolvedValueOnce('saved')
  render(<MobileSoldOut device={device} employee={employee} gateway={{
    adjust, read: async () => ({ items, canSetSoldOut: true, nextOffset: null }),
  }} />)
  await user.type(await screen.findByRole('textbox', { name: 'Item ID' }), 'I2')
  await user.click(screen.getByRole('button', { name: 'Set Sold Out' }))
  await user.click(await screen.findByRole('button', { name: 'Retry the same request' }))
  await screen.findByText('Sold Out change recorded.')
  expect(adjust.mock.calls[1][2]).toEqual(adjust.mock.calls[0][2])
  expect(adjust.mock.calls[0][2]).toMatchObject({ sellableItemId: 'I2', expectedSoldOut: false, soldOut: true })
})
