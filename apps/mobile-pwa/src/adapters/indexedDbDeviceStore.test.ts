// @vitest-environment node
import { IDBFactory } from 'fake-indexeddb'
import { describe, expect, it } from 'vitest'
import { FIXTURE_DEVICE } from './fixtures.ts'
import { IndexedDbDeviceStore } from './indexedDbDeviceStore.ts'

const CREDENTIAL = 'c'.repeat(32)

describe('IndexedDbDeviceStore', () => {
  it('starts empty', async () => {
    const store = new IndexedDbDeviceStore(new IDBFactory())
    expect(await store.load()).toBeNull()
    expect(await store.loadCredential(FIXTURE_DEVICE.deviceId)).toBeNull()
  })

  it('persists the device and its credential across store instances', async () => {
    const factory = new IDBFactory()
    await new IndexedDbDeviceStore(factory).save(FIXTURE_DEVICE, CREDENTIAL)

    const reopened = new IndexedDbDeviceStore(factory)
    expect(await reopened.load()).toEqual(FIXTURE_DEVICE)
    expect(await reopened.loadCredential(FIXTURE_DEVICE.deviceId)).toBe(CREDENTIAL)
  })

  it('returns no credential for a different device', async () => {
    const store = new IndexedDbDeviceStore(new IDBFactory())
    await store.save(FIXTURE_DEVICE, CREDENTIAL)
    expect(await store.loadCredential('another-device-id')).toBeNull()
  })

  it('replaces the record when the device is paired again', async () => {
    const store = new IndexedDbDeviceStore(new IDBFactory())
    await store.save(FIXTURE_DEVICE, CREDENTIAL)
    const repaired = { ...FIXTURE_DEVICE, deviceId: 'test-device-id-2' }
    await store.save(repaired, 'd'.repeat(32))

    expect(await store.load()).toEqual(repaired)
    expect(await store.loadCredential(FIXTURE_DEVICE.deviceId)).toBeNull()
    expect(await store.loadCredential('test-device-id-2')).toBe('d'.repeat(32))
  })
})
