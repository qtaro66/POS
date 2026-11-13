import type { MobileRequest } from '../../domain/mobileRequest.ts'
import type { AuthenticatedBranchRequest } from './http.ts'

export function createSubmitOrderGateway(request: AuthenticatedBranchRequest) {
  return async (device: Parameters<AuthenticatedBranchRequest>[0], employee: Parameters<AuthenticatedBranchRequest>[1],
    order: MobileRequest, query: boolean): Promise<'accepted' | 'rejected' | 'notFound' | 'unknown'> => {
    const path = query ? '/commands/' + encodeURIComponent(order.requestId) : '/orders'
    const body = query ? undefined : {
      schemaVersion: 1,
      context: { requestId: order.requestId, branchId: device.branchId, deviceId: order.deviceId,
        employeeId: order.employeeId, tableSessionId: order.tableSessionId, clientCreatedAt: order.clientCreatedAt },
      lines: order.lines.map(line => ({
        sellableItemId: line.sellableItemId, quantity: line.quantity, comment: line.comment,
        fulfillmentType: line.fulfillmentType ?? 'DineIn',
        options: line.selections.map(option => ({
          optionGroupId: option.optionGroupId, sellableItemId: option.optionId, quantity: option.quantity,
        })),
      })),
    }
    const response = await request(device, employee, '/api/v1/ordering' + path, undefined, body)
    const result = (response.value as { result?: { requestId?: string; status?: string } } | null)?.result
    if (result?.requestId !== order.requestId) return 'unknown'
    if (response.status === 200 && result.status === 'BranchAccepted') return 'accepted'
    if ((response.status === 200 || response.status === 409) && result.status === 'Rejected') return 'rejected'
    if (query && response.status === 404 && result.status === 'NoDurableResult') return 'notFound'
    return 'unknown'
  }
}
