import { describe, expect, it } from 'vitest'
import { alignRight, formatCell } from './reportFormat'

describe('report cells', () => {
  it('formats each kind of column', () => {
    expect(formatCell(1234, 'Number')).toBe('1,234')
    expect(formatCell(2.25, 'Number')).toBe('2.3')
    expect(formatCell(4500000, 'Money')).toBe('₦4,500,000')
    expect(formatCell(87.5, 'Percent')).toBe('87.5%')
    expect(formatCell('2026-03-01', 'Date')).toBe('01 Mar 2026')
    expect(formatCell('Total', 'Text')).toBe('Total')
  })

  it('shows a dash for empty cells', () => {
    expect(formatCell(null, 'Money')).toBe('–')
    expect(formatCell('', 'Text')).toBe('–')
  })

  it('right-aligns numbers only', () => {
    expect(alignRight('Money')).toBe(true)
    expect(alignRight('Date')).toBe(false)
  })
})
