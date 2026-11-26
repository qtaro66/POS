import { expect, it, vi } from 'vitest'
import { createOrderingClient } from './orderingClient.ts'
import { parseCashHistoryEventPage, validateCashHistoryEventQuery } from './cashHistoryEventContracts.ts'
import { cancellationEvidence, eventPage, sessionEvidence } from './cashHistoryEventTestFixtures.ts'

it('authenticates encoded evidence GETs, binds cursor/source and never sends a body', async () => {
  const fetcher = vi.fn(async (url: string | URL | Request) => new Response(JSON.stringify(eventPage(new URL(String(url)).searchParams.get('source') === 'Cancellation' ? 'Cancellation' : 'Session')), { headers: { 'Content-Type': 'application/json' } }))
  const client = createOrderingClient({ serviceOrigin: 'https://branch.example', branchId: 'branch', deviceId: 'device', employeeId: 'employee', deviceCredential: 'credential', employeeSessionToken: 'token' }, { fetch: fetcher as typeof fetch })
  expect((await client.cashHistoryEvents('CASH-1', { source: 'Cancellation', limit: 5, cursor: 'boundcursor' })).kind).toBe('ok')
  const [url, options] = fetcher.mock.calls[0] as unknown as [string, RequestInit]
  expect(new URL(url).searchParams.get('cursor')).toBe('boundcursor')
  expect(options.method).toBe('GET'); expect(options.body).toBeUndefined(); expect(JSON.stringify(options.headers)).toContain('token')
  await client.cashHistoryEvents('CASH/1'); expect(String(fetcher.mock.calls[1][0])).toContain('CASH%2F1/events')
  const count = fetcher.mock.calls.length
  expect((await client.cashHistoryEvents(' bad ')).kind).toBe('unavailable')
  expect((await client.cashHistoryEvents('CASH-1', { source: 'session' as never })).kind).toBe('unavailable')
  expect(fetcher).toHaveBeenCalledTimes(count)
})
it.each([{ source: null }, { source: 'All' }, { limit: 0 }, { limit: 51 }, { limit: 1.5 }, { cursor: '' }, { cursor: 'bad=' }])('rejects noncanonical evidence query %o', query => {
  expect(() => validateCashHistoryEventQuery(query as never)).toThrow()
})
it('accepts separate recorded sources and composite session IDs larger than an individual ID', () => {
  const event = sessionEvidence('a'.repeat(600))
  expect(parseCashHistoryEventPage(eventPage('Session', [event]), 'branch', 'CASH-1', {}, 'SESSION-1').events[0].eventId).toBe(event.eventId)
  const cancellation = cancellationEvidence()
  cancellation.cancellation!.lines.push({ orderLineId: 'NOT-CANCELLED', checked: { notStarted: 1, inProgress: 0, completed: 0, notApplicable: 0 }, cancelled: { notStarted: 0, inProgress: 0, completed: 0, notApplicable: 0 }, requestedQuantity: 1, notCancelledQuantity: 1 })
  expect(parseCashHistoryEventPage(eventPage('Cancellation', [cancellation]), 'branch', 'CASH-1', { source: 'Cancellation' }).events[0].deviceId).toBeNull()
})
it.each(['branch', 'cash', 'session', 'source', 'duplicate', 'prefix', 'device', 'action', 'status', 'version', 'reason'])('rejects contradictory session evidence: %s', mutation => {
  const page = eventPage(), event = page.events[0]
  if (mutation === 'branch') page.branchId = 'foreign'
  if (mutation === 'cash') page.cashRequestId = 'foreign'
  if (mutation === 'session') page.tableSessionId = 'foreign'
  if (mutation === 'source') page.source = 'Cancellation'
  if (mutation === 'duplicate') page.events.push(event)
  if (mutation === 'prefix') event.eventId = 'cancellation:wrong'
  if (mutation === 'device') event.deviceId = null
  if (mutation === 'action') event.session!.action = 'FutureAction'
  if (mutation === 'status') event.session!.currentStatus = 'FutureStatus'
  if (mutation === 'version') event.session!.tableVersion = Number.MAX_SAFE_INTEGER + 1
  if (mutation === 'reason') event.session!.reason = 'bad\u0085reason'
  expect(() => parseCashHistoryEventPage(page, 'branch', 'CASH-1', {}, 'SESSION-1')).toThrow()
})
it.each(['checked', 'cancelled', 'remaining', 'requested', 'outcome', 'zero', 'duplicate', 'identity', 'device', 'both'])('rejects inconsistent immutable cancellation evidence: %s', mutation => {
  const event = cancellationEvidence(), line = event.cancellation!.lines[0]
  if (mutation === 'checked') line.checked.completed++
  if (mutation === 'cancelled') line.cancelled.notStarted = 3
  if (mutation === 'remaining') line.notCancelledQuantity = 2
  if (mutation === 'requested') line.requestedQuantity = 2147483648
  if (mutation === 'outcome') event.cancellation!.outcome = 'Confirmed'
  if (mutation === 'zero') { line.cancelled.notStarted = 0; line.notCancelledQuantity = 4 }
  if (mutation === 'duplicate') event.cancellation!.lines.push(line)
  if (mutation === 'identity') line.orderLineId = 'bad\u0085id'
  if (mutation === 'device') event.deviceId = 'invented-current-device'
  if (mutation === 'both') event.session = sessionEvidence().session
  expect(() => parseCashHistoryEventPage(eventPage('Cancellation', [event]), 'branch', 'CASH-1', { source: 'Cancellation' })).toThrow()
})
it('does not fabricate success for malformed or empty-continuation pages', () => {
  expect(() => parseCashHistoryEventPage(eventPage('Session', [], 'cursor'), 'branch', 'CASH-1', {})).toThrow()
  expect(parseCashHistoryEventPage(eventPage('Session', []), 'branch', 'CASH-1', {}).events).toEqual([])
})
