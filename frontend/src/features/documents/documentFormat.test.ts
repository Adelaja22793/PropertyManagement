import { describe, expect, it } from 'vitest'
import { expiryState, formatSize } from './documentFormat'
import { todayInLagos } from '../../lib/format'

function addDays(days: number) {
  const d = new Date(`${todayInLagos()}T00:00:00Z`)
  d.setUTCDate(d.getUTCDate() + days)
  return d.toISOString().slice(0, 10)
}

describe('document formatting', () => {
  it('writes sizes people can read', () => {
    expect(formatSize(512)).toBe('512 B')
    expect(formatSize(820 * 1024)).toBe('820 KB')
    expect(formatSize(2.4 * 1024 * 1024)).toBe('2.4 MB')
  })

  it('flags lapsed and soon-to-lapse documents', () => {
    expect(expiryState(null)).toBeNull()
    expect(expiryState(addDays(-1))).toBe('expired')
    expect(expiryState(addDays(0))).toBe('soon')
    expect(expiryState(addDays(30))).toBe('soon')
    expect(expiryState(addDays(90))).toBe('ok')
  })
})
