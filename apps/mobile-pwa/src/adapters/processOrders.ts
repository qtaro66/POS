import { isOutstanding, newSubmitOrderRequest, transitionRequest, type MobileRequest } from '../domain/mobileRequest.ts'
import type { Draft } from '../domain/draft.ts'
import type { MobileServices } from './ports.ts'
import type { EmployeeSession, PairedDevice } from '../domain/types.ts'

export async function processOrders(
  services: Pick<MobileServices, 'outbox' | 'submitOrder'>,
  device: PairedDevice, employee: EmployeeSession, now: () => Date,
  draft?: Draft, signal?: AbortSignal,
): Promise<MobileRequest['state'] | undefined> {
  const { outbox, submitOrder } = services
  if (!outbox || !submitOrder) return
  return navigator.locks.request('pos-mobile-order-outbox', async () => {
    signal?.throwIfAborted()
    const requests = (await outbox.listRequests()).filter(order =>
      order.deviceId === device.deviceId && order.employeeId === employee.employeeId && isOutstanding(order))
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
      async function apply(type: Parameters<typeof transitionRequest>[1]['type']) {
        order = transitionRequest(order, { type }, now())
        await outbox!.updateRequest(order)
      }
      if (order.state === 'Submitted') await apply('resultUnknown')
      if (order.state === 'Unknown') {
        const result = await submitOrder!(device, employee, order, true).catch(() => 'unknown' as const)
        if (result === 'unknown') return order.state
        if (result !== 'notFound') {
          await apply(result)
          return order.state
        }
        await apply('notFound')
      }
      signal?.throwIfAborted()
      await apply('sendStarted')
      const result = await submitOrder!(device, employee, order, false).catch(() => 'unknown' as const)
      await apply(result === 'accepted' || result === 'rejected' ? result : 'resultUnknown')
      return order.state
    }
  })
}
