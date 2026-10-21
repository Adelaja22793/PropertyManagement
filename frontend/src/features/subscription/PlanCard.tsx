import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Subscription } from '../../lib/types'
import { formatDate } from '../../lib/format'
import { Badge, Card, ErrorBox } from '../../components/ui'
import { subscriptionStatusLabels, subscriptionTone } from './subscription'

/** The company's plan, where its subscription stands, and how much of the plan is in use. */
export function PlanCard() {
  const plan = useQuery({ queryKey: ['subscription'], queryFn: () => get<Subscription>('/api/v1/subscription') })
  const s = plan.data
  return (
    <Card title="Plan" actions={s && <Badge tone={subscriptionTone(s.status)}>{subscriptionStatusLabels[s.status]}</Badge>}>
      <ErrorBox error={plan.error} />
      {s && (
        <div className="space-y-4">
          <p className="text-sm text-slate-700">
            <strong>{s.planName}</strong>
            {s.status === 'Trial' && s.trialEndsAtUtc && <> · free trial until {formatDate(s.trialEndsAtUtc)}</>}
            {s.status === 'Active' && s.currentPeriodEndsAtUtc && <> · paid up to {formatDate(s.currentPeriodEndsAtUtc)}</>}
            {s.status === 'PastDue' && s.suspendsAtUtc && <> · lapsed, read-only from {formatDate(s.suspendsAtUtc)}</>}
          </p>
          <Usage label="Units" used={s.unitsUsed} max={s.maxUnits} />
          <Usage label="Team members (including pending invitations)" used={s.staffUsed} max={s.maxUsers} />
          <p className="text-xs text-slate-500">
            Owners and tenants with portal access don't count. To change plan or renew, contact us; online payment comes later.
          </p>
        </div>
      )}
    </Card>
  )
}

function Usage({ label, used, max }: { label: string; used: number; max: number }) {
  const share = max > 0 ? Math.min(100, Math.round((used / max) * 100)) : 100
  return (
    <div>
      <div className="flex justify-between text-sm">
        <span className="text-slate-700">{label}</span>
        <span className="font-medium text-slate-900">
          {used} of {max}
        </span>
      </div>
      <div
        className="mt-1 h-2 rounded-full bg-slate-100"
        role="progressbar"
        aria-label={label}
        aria-valuenow={used}
        aria-valuemin={0}
        aria-valuemax={max}
      >
        <div
          className={`h-2 rounded-full ${share >= 100 ? 'bg-red-500' : share >= 80 ? 'bg-amber-500' : 'bg-brand-600'}`}
          style={{ width: `${share}%` }}
        />
      </div>
    </div>
  )
}
