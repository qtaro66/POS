import { describe, expect, it } from 'vitest'
import { isSameRoute, isTableScreen, readShellRoute, type ShellRoute } from './shellRoute.ts'

const tableA: ShellRoute = {
  screen: 'table',
  table: { tableSessionId: 'session-a', tableLabel: 'A1' },
}
const menuA: ShellRoute = { screen: 'menu', table: tableA.table }
const newItem: ShellRoute = {
  screen: 'item',
  table: tableA.table,
  sellableItemId: 'item-1',
  lineId: null,
}
const editItem: ShellRoute = { ...newItem, lineId: 'line-1' }

describe('readShellRoute', () => {
  it.each([{ screen: 'tables' }, { screen: 'status' }, tableA, menuA, newItem, editItem])(
    'reads %o',
    (route) => {
      expect(readShellRoute(route)).toEqual(route)
    },
  )

  it.each([
    { screen: 'item', table: tableA.table, lineId: null },
    { screen: 'item', table: tableA.table, sellableItemId: 'item-1', lineId: 7 },
    { screen: 'menu' },
  ])('rejects the incomplete table route %o', (value) => {
    expect(readShellRoute(value)).toBeNull()
  })

  it('treats the menu and item screens as the current table’s screens', () => {
    expect([tableA, menuA, newItem].map(isTableScreen)).toEqual([true, true, true])
    expect(isTableScreen({ screen: 'status' })).toBe(false)
  })

  it('tells item screens apart by item and edited line', () => {
    expect(isSameRoute(newItem, { ...newItem })).toBe(true)
    expect(isSameRoute(newItem, editItem)).toBe(false)
    expect(isSameRoute(newItem, { ...newItem, sellableItemId: 'item-2' })).toBe(false)
    expect(isSameRoute(menuA, tableA)).toBe(false)
  })

  it('drops unknown fields', () => {
    expect(readShellRoute({ screen: 'status', extra: true })).toEqual({ screen: 'status' })
  })

  it.each([
    null,
    'tables',
    {},
    { screen: 'payment' },
    { screen: 'table' },
    { screen: 'table', table: { tableSessionId: 1, tableLabel: 'A1' } },
  ])('rejects %o', (value) => {
    expect(readShellRoute(value)).toBeNull()
  })
})

describe('isSameRoute', () => {
  it('compares tables by session', () => {
    expect(isSameRoute(tableA, { ...tableA })).toBe(true)
    expect(
      isSameRoute(tableA, {
        screen: 'table',
        table: { tableSessionId: 'session-b', tableLabel: 'A1' },
      }),
    ).toBe(false)
  })

  it('compares other screens by name', () => {
    expect(isSameRoute({ screen: 'tables' }, { screen: 'tables' })).toBe(true)
    expect(isSameRoute({ screen: 'tables' }, { screen: 'status' })).toBe(false)
    expect(isSameRoute({ screen: 'tables' }, tableA)).toBe(false)
  })
})
