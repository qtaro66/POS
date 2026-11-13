import { expect, it, vi } from 'vitest'
import { createSubmitOrderGateway } from './submitOrderGateway.ts'
import { FIXTURE_DEVICE } from '../fixtures.ts'
import { newSubmitOrderRequest } from '../../domain/mobileRequest.ts'

it.each([false, true])('maps Branch rejection sequences to stable draft IDs (query: %s)', async query => {
  const order = newSubmitOrderRequest('R1', {
    deviceId: FIXTURE_DEVICE.deviceId, employeeId: 'E1', tableSessionId: 'T1',
    sessionMasterDataVersion: 'V1', lines: ['one', 'two'].map(lineId => ({
      lineId, sellableItemId: 'I1', quantity: 1, selections: [], comment: '',
    })),
  }, new Date('2026-11-01T00:00:00Z'))
  const request = vi.fn().mockResolvedValue({ status: query ? 200 : 409, value: {
    result: { requestId: 'R1', status: 'Rejected', errorCode: 'CatalogValidationFailed' },
    validationDetails: { schemaVersion: 1, errors: [
      { lineSequence: 2, code: 'itemSoldOut' }, { lineSequence: 3, code: 'optionSoldOut' },
    ] },
  } })
  expect(await createSubmitOrderGateway(request)(FIXTURE_DEVICE, {
    employeeId: 'E1', displayName: 'Test', authenticatedAt: order.clientCreatedAt,
  }, order, query)).toEqual({ rejection: { code: 'CatalogValidationFailed', lines: { two: ['itemSoldOut'] } } })
})
