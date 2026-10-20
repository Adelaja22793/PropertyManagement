import { describe, expect, it } from 'vitest'
import { lagosInputToUtc, rentLabel, unitSummary, utcToLagosInput } from './listingFormat'

describe('listing formatting', () => {
  it('shows rent in whole naira with its period', () => {
    expect(rentLabel(4_500_000, 'Annual')).toBe('₦4,500,000 per year')
    expect(rentLabel(150_000, 'Monthly')).toBe('₦150,000 per month')
  })

  it('describes the space from its type and rooms', () => {
    expect(unitSummary('Flat', 2, 2)).toBe('2-bedroom flat · 2 bathrooms')
    expect(unitSummary('Duplex', 4, 1)).toBe('4-bedroom duplex · 1 bathroom')
    expect(unitSummary('Shop', null, null)).toBe('Shop')
  })

  it('converts viewing times between Lagos and UTC', () => {
    expect(lagosInputToUtc('2026-10-24T14:30')).toBe('2026-10-24T13:30:00.000Z')
    expect(utcToLagosInput('2026-10-24T13:30:00Z')).toBe('2026-10-24T14:30')
    expect(utcToLagosInput('2026-12-31T23:30:00')).toBe('2027-01-01T00:30')
  })
})
