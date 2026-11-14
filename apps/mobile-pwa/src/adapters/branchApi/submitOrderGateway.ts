import type { MobileRequest, OrderRejection } from '../../domain/mobileRequest.ts'
import type { AuthenticatedBranchRequest } from './http.ts'

type SubmitOrderResult = 'accepted' | 'notFound' | 'unknown' | { rejection: OrderRejection }

function rejectionDetails(value: unknown, order: MobileRequest, code: unknown): OrderRejection {
  const details = (value as { validationDetails?: { schemaVersion?: unknown; errors?: unknown } } | null)?.validationDetails
  const lines: Record<string, string[]> = Object.create(null)
  if (details?.schemaVersion === 1 && Array.isArray(details.errors)) {
    for (const entry of details.errors) {
      if (!Number.isInteger(entry?.lineSequence) || typeof entry.code !== 'string') continue
      const line = order.lines[entry.lineSequence - 1]
      if (line) (lines[line.lineId] ??= []).push(entry.code)
    }
  }
  return { code: typeof code === 'string' ? code : undefined, lines }
}

export function createSubmitOrderGateway(request: AuthenticatedBranchRequest) {
  return async (device: Parameters<AuthenticatedBranchRequest>[0], employee: Parameters<AuthenticatedBranchRequest>[1],
    order: MobileRequest, query: boolean): Promise<SubmitOrderResult> => {
    const path = query ? '/commands/' + encodeURIComponent(order.requestId) : order.command === 'RequestPayment' ? '/tables/request-payment' : order.command === 'RequestCancellation' ? '/cancellation-requests' : '/orders'
    const body = query ? undefined : {
      schemaVersion: 1,
      context: { requestId: order.requestId, branchId: device.branchId, deviceId: order.deviceId,
        employeeId: order.employeeId, tableSessionId: order.tableSessionId, clientCreatedAt: order.clientCreatedAt },
      ...(order.command === 'RequestPayment' ? { expectedVersion: order.expectedVersion } : order.command === 'RequestCancellation' ? { lines: order.cancellationLines } : { lines: order.lines.map(line => ({
        sellableItemId: line.sellableItemId, quantity: line.quantity, comment: line.comment,
        fulfillmentType: line.fulfillmentType ?? 'DineIn',
        options: line.selections.map(option => ({
          optionGroupId: option.optionGroupId, sellableItemId: option.optionId, quantity: option.quantity,
        })),
      })) }),
    }
    const response = await request(device, employee, '/api/v1/ordering' + path, undefined, body)
    const result = (response.value as { result?: { requestId?: string; status?: string; errorCode?: unknown } } | null)?.result
    if (result?.requestId !== order.requestId) return 'unknown'
    if (response.status === 200 && result.status === 'BranchAccepted') return 'accepted'
    if ((response.status === 200 || response.status === 409) && result.status === 'Rejected') return { rejection: rejectionDetails(response.value, order, result.errorCode) }
    if (query && response.status === 404 && result.status === 'NoDurableResult') return 'notFound'
    return 'unknown'
  }
}
