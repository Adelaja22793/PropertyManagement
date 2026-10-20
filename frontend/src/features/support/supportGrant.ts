import type { SupportGrant, SupportGrantStatus } from '../../lib/types'
import { formatDate } from '../../lib/format'

export const statusTone: Record<SupportGrantStatus, 'slate' | 'green' | 'amber' | 'red'> = {
  Pending: 'amber',
  Active: 'green',
  Declined: 'slate',
  Revoked: 'slate',
  Expired: 'slate',
}

/** One line on when the access starts, ends or ended. */
export function grantTiming(g: SupportGrant): string {
  const hours = `${g.hours} ${g.hours === 1 ? 'hour' : 'hours'}`
  switch (g.status) {
    case 'Pending':
      return `Asked ${formatDate(g.requestedAtUtc)} for ${hours}`
    case 'Active':
      return `Read-only until ${formatDate(g.expiresAtUtc!)}`
    case 'Declined':
      return `Declined ${formatDate(g.decidedAtUtc!)}${g.decidedByName ? ` by ${g.decidedByName}` : ''}`
    case 'Revoked':
      return g.revokedAtUtc ? `Ended ${formatDate(g.revokedAtUtc)}` : 'Ended'
    case 'Expired':
      return `Ran out ${formatDate(g.expiresAtUtc!)}`
  }
}
