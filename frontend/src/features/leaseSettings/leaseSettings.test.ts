import { describe, expect, it } from 'vitest'
import { parseDays, ruleScope } from './leaseSettings'

describe('parseDays', () => {
  it('reads, deduplicates and sorts days', () => {
    expect(parseDays('30, 90 60,30')).toEqual([90, 60, 30])
  })

  it('explains what is wrong', () => {
    expect(parseDays('')).toMatch(/at least one/)
    expect(parseDays('thirty')).toMatch(/whole numbers/)
    expect(parseDays('400')).toMatch(/between 1 and 365/)
    expect(parseDays('1, 2, 3, 4, 5, 6')).toMatch(/at most 5/)
  })
})

describe('ruleScope', () => {
  it('describes where a notice period applies', () => {
    expect(ruleScope(null, null)).toBe('All states, any rent frequency')
    expect(ruleScope('Ogun', 'Annual')).toBe('Ogun, yearly rent')
  })
})
