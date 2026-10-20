import { describe, expect, it } from 'vitest'
import { defaultEndDate, moveInTotal, nextDay, rentChangePercent } from './leaseMath'

describe('lease dates', () => {
  it('ends a one-year term the day before the anniversary', () => {
    expect(defaultEndDate('2026-03-01')).toBe('2027-02-28')
    expect(defaultEndDate('2026-01-15', 2)).toBe('2028-01-14')
    expect(defaultEndDate('2028-02-29')).toBe('2029-02-28')
  })

  it('finds the next day across month and year ends', () => {
    expect(nextDay('2026-12-31')).toBe('2027-01-01')
    expect(nextDay('2028-02-28')).toBe('2028-02-29')
  })
})

describe('move-in total', () => {
  it('adds the first rent, one-off charges and the first period of recurring charges', () => {
    expect(
      moveInTotal(3_000_000, [
        { amount: 300_000, recurrence: 'OneOff' },
        { amount: 300_000, recurrence: 'OneOff' },
        { amount: 250_000.5, recurrence: 'EachPeriod' },
      ]),
    ).toBe(3_850_000.5)
    expect(moveInTotal(1_200_000, [])).toBe(1_200_000)
  })
})

describe('rent change', () => {
  it('reports the review as a percentage', () => {
    expect(rentChangePercent(3_000_000, 3_600_000)).toBe(20)
    expect(rentChangePercent(3_000_000, 2_850_000)).toBe(-5)
    expect(rentChangePercent(0, 100)).toBe(0)
  })
})
