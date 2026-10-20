/** One row of the ownership editor: the owner and their share as typed. */
export interface ShareRow {
  ownerId: string
  percent: string
}

/** A typed share in hundredths of a percent (so 33.33% is 3333), or null when it isn't a valid share. */
export function shareHundredths(percent: string): number | null {
  const text = percent.trim()
  if (!/^\d{1,3}(\.\d{1,2})?$/.test(text)) return null
  const value = Math.round(Number(text) * 100)
  return value > 0 && value <= 10000 ? value : null
}

/** The total of the valid shares, in percent. Summed in hundredths so 33.33 + 33.33 + 33.34 is exactly 100. */
export function ownershipTotal(rows: ShareRow[]): number {
  return rows.reduce((sum, r) => sum + (shareHundredths(r.percent) ?? 0), 0) / 100
}

/** Why the rows can't be saved yet, or null when they can: every owner chosen once, valid shares, exactly 100%. */
export function ownershipProblem(rows: ShareRow[]): string | null {
  if (rows.length === 0) return 'Add at least one owner.'
  if (rows.some((r) => !r.ownerId)) return 'Choose an owner on every row.'
  if (new Set(rows.map((r) => r.ownerId)).size !== rows.length) return 'Each owner can appear only once.'
  if (rows.some((r) => shareHundredths(r.percent) === null)) return 'Each share must be more than 0 and at most 100, with up to two decimals.'
  const total = rows.reduce((sum, r) => sum + shareHundredths(r.percent)!, 0)
  if (total !== 10000) return `Shares must total exactly 100%. They total ${total / 100}%.`
  return null
}
