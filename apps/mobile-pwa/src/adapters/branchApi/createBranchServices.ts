import { createFixtureServices } from '../fixtures.ts'
import { IndexedDbDeviceStore } from '../indexedDbDeviceStore.ts'
import type { MobileServices } from '../ports.ts'
import { BranchEmployeeAuthGateway, BranchPairingGateway } from './branchGateways.ts'
import { createAuthenticatedBranchRequest, type FetchLike } from './http.ts'
import { createOrderingReadGateways } from './orderingReadGateways.ts'
import { createSubmitOrderGateway } from './submitOrderGateway.ts'
import { createSoldOutGateway } from './soldOutGateway.ts'

/**
 * Branch-backed pairing, login, active tables, menu and Sold Out.
 * QR camera and sync status still use fixtures.
 */
export function createBranchServices(
  fetchImpl: FetchLike = (input, init) => fetch(input, init),
): MobileServices {
  const deviceStore = new IndexedDbDeviceStore()
  const fixtures = createFixtureServices()
  const options = { fetch: fetchImpl, deviceStore }
  const auth = new BranchEmployeeAuthGateway(options)
  const request = createAuthenticatedBranchRequest(fetchImpl, deviceStore, id => auth.sessionTokenFor(id))
  return {
    ...fixtures,
    deviceStore,
    pairing: new BranchPairingGateway(options),
    auth,
    ...createOrderingReadGateways(request),
    submitOrder: createSubmitOrderGateway(request),
    soldOut: createSoldOutGateway(request),
  }
}
