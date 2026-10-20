import { Link } from 'react-router-dom'
import type { MaintenanceSummary } from '../../lib/types'
import { enumLabel, formatDate, formatNaira } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge } from '../../components/ui'

export function StatusBadge({ status }: { status: string }) {
  return <Badge tone={statusTone(status)}>{enumLabel(status)}</Badge>
}

export function UrgencyBadge({ urgency }: { urgency: string }) {
  return <Badge tone={statusTone(urgency)}>{urgency}</Badge>
}

/** Maintenance requests as a list of links. Shared by the maintenance page and the property page. */
export function MaintenanceList({ items, showProperty = true }: { items: MaintenanceSummary[]; showProperty?: boolean }) {
  return (
    <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
      {items.map((r) => (
        <li key={r.id}>
          <Link to={`/app/maintenance/${r.id}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 hover:bg-slate-50">
            <div className="min-w-0">
              <p className="text-sm font-medium text-slate-900">
                {r.title}
                <span className="ml-2 text-xs font-normal text-slate-500">{r.reference}</span>
              </p>
              <p className="truncate text-sm text-slate-600">
                {showProperty ? `${r.propertyName}` : ''}
                {r.unitCode ? `${showProperty ? ' · ' : ''}Unit ${r.unitCode}` : ''}
                {(showProperty || r.unitCode) && ' · '}
                {enumLabel(r.category)} · reported {formatDate(r.createdAtUtc)}
              </p>
              {(r.assignedToName || r.vendorName) && (
                <p className="truncate text-xs text-slate-500">
                  {[r.assignedToName, r.vendorName].filter(Boolean).join(' and ')}
                </p>
              )}
            </div>
            <div className="flex flex-wrap items-center gap-2 text-sm">
              {r.estimateAmount !== null && <span className="text-slate-700 tabular-nums">{formatNaira(r.estimateAmount)}</span>}
              {r.awaitingOwnerApproval && <Badge tone="amber">Owner approval needed</Badge>}
              <UrgencyBadge urgency={r.urgency} />
              <StatusBadge status={r.status} />
            </div>
          </Link>
        </li>
      ))}
    </ul>
  )
}
