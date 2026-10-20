import { describe, expect, it } from 'vitest'
import type { SupportGrant } from '../../lib/types'
import { grantTiming } from './supportGrant'

const grant = (over: Partial<SupportGrant>): SupportGrant => ({
  id: 'g',
  organizationId: 'o',
  organizationName: 'Ikoyi Homes',
  requestedByName: 'Tobi',
  requestedByEmail: 'tobi@example.com',
  reason: 'Ticket 12',
  hours: 1,
  status: 'Pending',
  requestedAtUtc: '2026-10-20T09:00:00Z',
  decidedByName: null,
  decidedAtUtc: null,
  expiresAtUtc: null,
  revokedAtUtc: null,
  ...over,
})

describe('support grant timing', () => {
  it('says how long was asked for while pending', () => {
    expect(grantTiming(grant({}))).toMatch(/^Asked .* for 1 hour$/)
    expect(grantTiming(grant({ hours: 4 }))).toMatch(/for 4 hours$/)
  })

  it('says when active access ends and who declined', () => {
    expect(grantTiming(grant({ status: 'Active', expiresAtUtc: '2026-10-20T11:00:00Z' }))).toMatch(/^Read-only until /)
    expect(grantTiming(grant({ status: 'Declined', decidedAtUtc: '2026-10-20T09:05:00Z', decidedByName: 'Seun' }))).toMatch(/by Seun$/)
  })
})
