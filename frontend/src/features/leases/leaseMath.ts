import type { ChargeRecurrence } from '../../lib/types'

/** The last day of a term that starts on `start` ('YYYY-MM-DD') and runs for whole years. */
export function defaultEndDate(start: string, years = 1): string {
  const [y, m, d] = start.split('-').map(Number)
  const end = new Date(Date.UTC(y + years, m - 1, d))
  // 29 Feb plus a year rolls to 1 Mar; either way the term ends the day before the anniversary.
  end.setUTCDate(end.getUTCDate() - 1)
  return end.toISOString().slice(0, 10)
}

/** The day after `day`, as 'YYYY-MM-DD'. */
export function nextDay(day: string): string {
  const date = new Date(`${day}T00:00:00Z`)
  date.setUTCDate(date.getUTCDate() + 1)
  return date.toISOString().slice(0, 10)
}

/**
 * What the tenant pays to move in: the first period's rent, every one-off charge, and the first period of each
 * recurring charge. A preview only; Phase 4 finance produces the actual invoice once the design is signed off.
 */
export function moveInTotal(rent: number, charges: { amount: number; recurrence: ChargeRecurrence }[]): number {
  return Math.round((rent + charges.reduce((sum, c) => sum + c.amount, 0)) * 100) / 100
}

/** A rent change as a percentage, rounded to one decimal place, e.g. 20 for 3.0m → 3.6m. */
export function rentChangePercent(oldRent: number, newRent: number): number {
  return oldRent === 0 ? 0 : Math.round(((newRent - oldRent) / oldRent) * 1000) / 10
}

/** How long until a lease ends, in words, for active leases. */
export function expiryText(daysToExpiry: number | null): string | null {
  if (daysToExpiry === null) return null
  if (daysToExpiry < 0) return `Ended ${-daysToExpiry} ${-daysToExpiry === 1 ? 'day' : 'days'} ago`
  if (daysToExpiry === 0) return 'Ends today'
  return `Ends in ${daysToExpiry} ${daysToExpiry === 1 ? 'day' : 'days'}`
}
