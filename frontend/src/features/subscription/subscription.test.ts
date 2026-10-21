import { describe, expect, it } from 'vitest'
import { trialEndingSoon } from './subscription'

const base = { currentPeriodEndsAtUtc: null, suspendsAtUtc: null, exportUntilUtc: null }
const now = new Date('2026-10-21T12:00:00Z')

describe('trialEndingSoon', () => {
  it('is true only for a trial that ends within a week', () => {
    expect(trialEndingSoon({ ...base, status: 'Trial', trialEndsAtUtc: '2026-10-25T12:00:00Z' }, now)).toBe(true)
    expect(trialEndingSoon({ ...base, status: 'Trial', trialEndsAtUtc: '2026-11-15T12:00:00Z' }, now)).toBe(false)
    expect(trialEndingSoon({ ...base, status: 'Active', trialEndsAtUtc: '2026-10-25T12:00:00Z' }, now)).toBe(false)
  })
})
