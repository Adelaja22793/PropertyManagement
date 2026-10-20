import type { Me, OrganizationSummary } from '../../lib/types'

/** When the member must have two-step sign-in on for this organization, or null if their roles don't need it. */
export function twoFactorDeadline(me: Me, organization: OrganizationSummary): Date | null {
  if (me.twoFactorEnabled || !organization.twoFactorRequiredSinceUtc) return null
  const since = organization.twoFactorRequiredSinceUtc
  const start = new Date(since.endsWith('Z') ? since : `${since}Z`)
  return new Date(start.getTime() + me.twoFactorGraceDays * 24 * 60 * 60 * 1000)
}

/** Recovery codes as a text file to save or print. */
export function recoveryCodesFile(email: string, codes: string[]): string {
  return [
    'Property Management recovery codes',
    `Account: ${email}`,
    '',
    'Each code signs you in once if you lose your phone. Keep them somewhere safe.',
    '',
    ...codes,
    '',
  ].join('\n')
}

/** Whether the deadline has passed. */
export function isOverdue(deadline: Date | null, now = Date.now()): boolean {
  return deadline !== null && deadline.getTime() <= now
}
