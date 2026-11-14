import { isOutstanding, newSubmitOrderRequest, transitionRequest, type MobileRequest, type CancellationSelection } from '../domain/mobileRequest.ts'
import type { Draft } from '../domain/draft.ts'
import type { MobileServices } from './ports.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'

export async function processOrders(
  services: Pick<MobileServices, 'outbox' | 'submitOrder'>,
  device: PairedDevice, employee: EmployeeSession, now: () => Date,
  draft?: Draft, signal?: AbortSignal, payment?: { tableSessionId: string; version: number },
  cancellation?: { tableSessionId: string; lines: readonly CancellationSelection[] },
): Promise<MobileRequest['state'] | undefined> {
  const { outbox, submitOrder } = services
  if (!outbox || !submitOrder) return
  return navigator.locks.request('pos-mobile-order-outbox', async () => {
    signal?.throwIfAborted()
    const requests = (await outbox.listRequests()).filter(order =>
      order.deviceId === device.deviceId && order.employeeId === employee.employeeId && isOutstanding(order))
    if (cancellation) {
      if (!outbox.queueCancellation || requests.some(order => order.tableSessionId === cancellation.tableSessionId)) return
      const at = now().toISOString()
      const order: MobileRequest = {
        requestId: crypto.randomUUID(), command: 'RequestCancellation', deviceId: device.deviceId,
        employeeId: employee.employeeId, tableSessionId: cancellation.tableSessionId,
        cancellationLines: cancellation.lines, sessionMasterDataVersion: '', lines: [], state: 'Pending',
        clientCreatedAt: at, updatedAt: at,
      }
      await outbox.queueCancellation(order)
      return process(order)
    }
    if (payment) {
      if (!outbox.queuePayment || requests.some(order => order.tableSessionId === payment.tableSessionId)) return
      const at = now()
      const order: MobileRequest = {
        requestId: crypto.randomUUID(), command: 'RequestPayment', deviceId: device.deviceId,
        employeeId: employee.employeeId, tableSessionId: payment.tableSessionId,
        expectedVersion: payment.version, sessionMasterDataVersion: '', lines: [], state: 'Pending',
        clientCreatedAt: at.toISOString(), updatedAt: at.toISOString(),
      }
      await outbox.queuePayment(order, order)
      return process(order)
    }
    if (draft) {
      if (requests.some(order => order.tableSessionId === draft.tableSessionId)) return 'Unknown'
      const order = newSubmitOrderRequest(crypto.randomUUID(), draft, now())
      await outbox.submitDraft(draft, order)
      return process(order)
    }
    for (const order of requests) {
      signal?.throwIfAborted()
      await process(order)
    }

    async function process(initial: MobileRequest) {
      let order = initial
      async function apply(type: Parameters<typeof transitionRequest>[1]['type'], rejection?: MobileRequest['rejection']) {
        order = transitionRequest(order, { type }, now())
        if (rejection) order = { ...order, rejection }
        await outbox!.updateRequest(order)
      }
      if (order.state === 'Submitted') await apply('resultUnknown')
      if (order.state === 'Unknown') {
        const result = await submitOrder!(device, employee, order, true).catch(() => 'unknown' as const)
        if (result === 'unknown') return order.state
        if (result !== 'notFound') {
          if (typeof result === 'object') await apply('rejected', result.rejection)
          else await apply(result)
          return order.state
        }
        await apply('notFound')
      }
      signal?.throwIfAborted()
      await apply('sendStarted')
      const result = await submitOrder!(device, employee, order, false).catch(() => 'unknown' as const)
      if (typeof result === 'object') await apply('rejected', result.rejection)
      else await apply(result === 'accepted' ? result : 'resultUnknown')
      return order.state
    }
  })
}
