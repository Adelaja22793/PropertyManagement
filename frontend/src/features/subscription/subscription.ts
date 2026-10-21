import type { SubscriptionStatus, SubscriptionSummary } from '../../lib/types'

/** Days before the trial ends when the banner appears; matches the reminder email. */
export const trialReminderDays = 7

export const subscriptionStatusLabels: Record<SubscriptionStatus, string> = {
  Trial: 'Free trial',
  Active: 'Active',
  PastDue: 'Lapsed',
  Suspended: 'Suspended (read-only)',
  Cancelled: 'Cancelled',
}

export const subscriptionTone = (status: SubscriptionStatus): 'green' | 'amber' | 'red' | 'slate' =>
  status === 'Active' ? 'green' : status === 'Trial' ? 'slate' : status === 'PastDue' ? 'amber' : 'red'

/** Whether the trial ends within the reminder window. */
export function trialEndingSoon(s: SubscriptionSummary, now = new Date()): boolean {
  if (s.status !== 'Trial' || !s.trialEndsAtUtc) return false
  const left = new Date(s.trialEndsAtUtc).getTime() - now.getTime()
  return left <= trialReminderDays * 24 * 60 * 60 * 1000
}
