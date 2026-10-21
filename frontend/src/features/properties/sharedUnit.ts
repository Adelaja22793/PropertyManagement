import type { Unit } from '../../lib/types'

/** True when a unit can take another lease: it is vacant, or it is shared and below its limit. */
export function hasRoom(u: Pick<Unit, 'occupancyStatus' | 'sharedOccupancyLimit' | 'activeLeaseCount'>): boolean {
  if (u.sharedOccupancyLimit === null) return u.occupancyStatus === 'Vacant'
  return u.occupancyStatus !== 'Unavailable' && u.activeLeaseCount < u.sharedOccupancyLimit
}

/** "Shared, 1 of 3 let" for a shared unit; null for an ordinary one. */
export function sharedLabel(u: Pick<Unit, 'sharedOccupancyLimit' | 'activeLeaseCount'>): string | null {
  return u.sharedOccupancyLimit === null ? null : `Shared, ${u.activeLeaseCount} of ${u.sharedOccupancyLimit} let`
}
