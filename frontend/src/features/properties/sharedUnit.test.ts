import { describe, expect, it } from 'vitest'
import { hasRoom, sharedLabel } from './sharedUnit'

describe('shared units', () => {
  it('an ordinary unit has room only while vacant', () => {
    expect(hasRoom({ occupancyStatus: 'Vacant', sharedOccupancyLimit: null, activeLeaseCount: 0 })).toBe(true)
    expect(hasRoom({ occupancyStatus: 'Occupied', sharedOccupancyLimit: null, activeLeaseCount: 1 })).toBe(false)
    expect(sharedLabel({ sharedOccupancyLimit: null, activeLeaseCount: 1 })).toBeNull()
  })

  it('a shared unit has room until its limit', () => {
    expect(hasRoom({ occupancyStatus: 'Occupied', sharedOccupancyLimit: 3, activeLeaseCount: 2 })).toBe(true)
    expect(hasRoom({ occupancyStatus: 'Occupied', sharedOccupancyLimit: 3, activeLeaseCount: 3 })).toBe(false)
    expect(hasRoom({ occupancyStatus: 'Unavailable', sharedOccupancyLimit: 3, activeLeaseCount: 0 })).toBe(false)
    expect(sharedLabel({ sharedOccupancyLimit: 3, activeLeaseCount: 1 })).toBe('Shared, 1 of 3 let')
  })
})
