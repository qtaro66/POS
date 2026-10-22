import { createFixtureServices } from '../fixtures.ts'
import { IndexedDbDeviceStore } from '../indexedDbDeviceStore.ts'
import type { MobileServices } from '../ports.ts'
import { BranchEmployeeAuthGateway, BranchPairingGateway } from './branchGateways.ts'
import type { FetchLike } from './http.ts'

/**
 * Services that pair and log in through the Branch API. Ports without a
 * Branch endpoint or device implementation yet (QR camera, active tables,
 * sync status) still use the fixtures.
 */
export function createBranchServices(
  fetchImpl: FetchLike = (input, init) => fetch(input, init),
): MobileServices {
  const deviceStore = new IndexedDbDeviceStore()
  const fixtures = createFixtureServices()
  const options = { fetch: fetchImpl, deviceStore }
  return {
    ...fixtures,
    deviceStore,
    pairing: new BranchPairingGateway(options),
    auth: new BranchEmployeeAuthGateway(options),
  }
}
