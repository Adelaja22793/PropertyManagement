import { Link } from 'react-router-dom'
import type { LeaseSummary } from '../../lib/types'
import { enumLabel, formatDay, formatNaira } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge } from '../../components/ui'
import { expiryText } from './leaseMath'

export function StageBadge({ stage }: { stage: string }) {
  return <Badge tone={statusTone(stage)}>{enumLabel(stage)}</Badge>
}

/** Leases as a list of links. Shared by the leases page, property and tenant pages. */
export function LeaseList({ leases, showProperty = true }: { leases: LeaseSummary[]; showProperty?: boolean }) {
  return (
    <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
      {leases.map((l) => (
        <li key={l.id}>
          <Link to={`/app/leases/${l.id}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 hover:bg-slate-50">
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-900">
                {l.primaryTenantName ?? 'No tenant'}
                {l.tenantCount > 1 && <span className="font-normal text-slate-500"> +{l.tenantCount - 1}</span>}
                <span className="ml-2 text-xs font-normal text-slate-500">{l.reference}</span>
              </p>
              <p className="truncate text-sm text-slate-600">
                {showProperty ? `${l.propertyName} · ` : ''}Unit {l.unitCode} · {formatDay(l.startDate)} to {formatDay(l.endDate)}
              </p>
            </div>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              <span className="text-slate-700 tabular-nums">
                {formatNaira(l.rentAmount)} <span className="text-xs text-slate-500">{enumLabel(l.billingFrequency).toLowerCase()}</span>
              </span>
              {(l.stage === 'Expiring' || l.stage === 'HoldingOver') && (
                <span className="text-xs text-slate-500">{expiryText(l.daysToExpiry)}</span>
              )}
              {l.renewalLeaseId && l.status === 'Active' && <Badge>Renewal drafted</Badge>}
              <StageBadge stage={l.stage} />
            </div>
          </Link>
        </li>
      ))}
    </ul>
  )
}
