import type { BillingFrequency, NoticeType } from '../../lib/types'

export const leaseSettingsKey = ['lease-settings']

/** Reads "90, 60, 30" into whole days, largest first. Returns an error message instead when the text isn't valid. */
export function parseDays(text: string): number[] | string {
  const parts = text
    .split(/[,\s]+/)
    .map((p) => p.trim())
    .filter(Boolean)
  if (parts.length === 0) return 'Enter at least one number of days.'
  if (parts.some((p) => !/^\d+$/.test(p))) return 'Use whole numbers separated by commas, such as 90, 60, 30.'
  const days = [...new Set(parts.map(Number))].sort((a, b) => b - a)
  if (days.some((d) => d < 1 || d > 365)) return 'Each number must be between 1 and 365.'
  if (days.length > 5) return 'Choose at most 5 days.'
  return days
}

export const formatDays = (days: number[]) => days.join(', ')

export const noticeTypeLabels: Record<NoticeType, string> = {
  NoticeToQuit: 'Notice to quit',
  OwnersIntentionToRecoverPossession: "Owner's intention to recover possession",
  RentReview: 'Rent review',
  Other: 'Other notice',
}

export const frequencyLabels: Record<BillingFrequency, string> = {
  Annual: 'yearly rent',
  Biannual: 'twice-yearly rent',
  Quarterly: 'quarterly rent',
  Monthly: 'monthly rent',
}

/** "All states, yearly rent" style description of who a notice period applies to. */
export function ruleScope(state: string | null, frequency: BillingFrequency | null): string {
  return `${state ?? 'All states'}, ${frequency ? frequencyLabels[frequency] : 'any rent frequency'}`
}
