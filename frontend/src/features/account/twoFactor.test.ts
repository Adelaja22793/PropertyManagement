import { describe, expect, it } from 'vitest'
import type { Me, OrganizationSummary } from '../../lib/types'
import { isOverdue, recoveryCodesFile, twoFactorDeadline } from './twoFactor'

const org = (since: string | null): OrganizationSummary => ({
  id: 'o',
  name: 'Ikoyi Homes',
  slug: 'ikoyi',
  type: 'ManagementCompany',
  status: 'Active',
  roles: ['OrganizationAdmin'],
  permissions: [],
  twoFactorRequiredSinceUtc: since,
})
const me = (enabled: boolean): Me => ({
  userId: 'u',
  email: 'seun@example.com',
  fullName: 'Seun',
  isPlatformAdmin: false,
  organizations: [],
  twoFactorEnabled: enabled,
  twoFactorGraceDays: 7,
})

describe('two-step sign-in deadline', () => {
  it('is the grace period after the role was given', () => {
    expect(twoFactorDeadline(me(false), org('2026-10-01T09:00:00'))?.toISOString()).toBe('2026-10-08T09:00:00.000Z')
  })

  it('is null when it is already on or not needed', () => {
    expect(twoFactorDeadline(me(true), org('2026-10-01T09:00:00Z'))).toBeNull()
    expect(twoFactorDeadline(me(false), org(null))).toBeNull()
  })

  it('is overdue once the deadline has passed', () => {
    const deadline = new Date('2026-10-08T09:00:00Z')
    expect(isOverdue(deadline, Date.parse('2026-10-08T08:59:59Z'))).toBe(false)
    expect(isOverdue(deadline, Date.parse('2026-10-08T09:00:00Z'))).toBe(true)
    expect(isOverdue(null)).toBe(false)
  })

  it('writes recovery codes one per line', () => {
    expect(recoveryCodesFile('seun@example.com', ['aaaa-bbbb', 'cccc-dddd'])).toContain('aaaa-bbbb\ncccc-dddd')
  })
})
