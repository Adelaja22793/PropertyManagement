import type { OrganizationSummary } from '../../lib/types'
import { formatDate } from '../../lib/format'
import { trialEndingSoon } from './subscription'

/** Tells staff where the company's subscription stands when it needs attention (design 4.8). */
export function SubscriptionBanner({ organization }: { organization: OrganizationSummary }) {
  const s = organization.subscription
  if (!s) return null
  if (s.status === 'Suspended')
    return (
      <div role="status" className="border-b border-red-200 bg-red-50 px-4 py-2.5 text-sm text-red-900 sm:px-6">
        {organization.name} is read-only because its subscription is suspended. You can look at records but not change them. Owners and
        tenants can still use their portals.
      </div>
    )
  if (s.status === 'PastDue' && s.suspendsAtUtc)
    return (
      <div role="status" className="border-b border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-900 sm:px-6">
        The subscription for {organization.name} has lapsed. Everything keeps working until {formatDate(s.suspendsAtUtc)}, then it becomes
        read-only until it is renewed.
      </div>
    )
  if (trialEndingSoon(s) && s.trialEndsAtUtc)
    return (
      <div role="status" className="border-b border-sky-200 bg-sky-50 px-4 py-2.5 text-sm text-sky-900 sm:px-6">
        The free trial for {organization.name} ends on {formatDate(s.trialEndsAtUtc)}. Contact us to choose a plan and keep full access.
      </div>
    )
  return null
}
