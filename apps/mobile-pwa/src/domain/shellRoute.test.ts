import { describe, expect, it } from 'vitest'
import { isSameRoute, readShellRoute, type ShellRoute } from './shellRoute.ts'

const tableA: ShellRoute = {
  screen: 'table',
  table: { tableSessionId: 'session-a', tableLabel: 'A1' },
}

describe('readShellRoute', () => {
  it.each([{ screen: 'tables' }, { screen: 'status' }, tableA])('reads %o', (route) => {
    expect(readShellRoute(route)).toEqual(route)
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
