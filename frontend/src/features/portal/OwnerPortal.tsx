import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { OwnerPortal as OwnerPortalData, PortalRequestSummary } from '../../lib/types'
import { enumLabel, formatDay, formatNaira } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Card, Empty, ErrorBox } from '../../components/ui'
import { MessageForm, RequestDetail, RequestList } from './PortalRequests'
import { useOpenRequest } from './useOpenRequest'

/** What a landlord sees: their properties and who is in each unit, and repairs, with approvals to give. */
export function OwnerPortal() {
  const [open, setOpen] = useOpenRequest()
  const home = useQuery({ queryKey: ['portal', 'owner'], queryFn: () => get<OwnerPortalData>('/api/v1/portal/owner') })
  const requests = useQuery({
    queryKey: ['portal', 'owner', 'maintenance'],
    queryFn: () => get<PortalRequestSummary[]>('/api/v1/portal/owner/maintenance'),
  })

  if (open) {
    return (
      <RequestDetail path={`/api/v1/portal/owner/maintenance/${open}`} onBack={() => setOpen(null)}>
        {(r, refresh) =>
          r.awaitingOwnerApproval && (
            <Card title="Your approval is needed">
              <p className="mb-4 text-sm text-slate-700">
                The estimate of {formatNaira(r.estimateAmount)} is above your repair approval limit of {formatNaira(r.approvalLimit)}. The work goes
                ahead once you approve it.
              </p>
              <div className="grid gap-6 sm:grid-cols-2">
                <MessageForm
                  label="Note (optional)"
                  button="Approve estimate"
                  required={false}
                  path={`/api/v1/portal/owner/maintenance/${r.id}/approve`}
                  body={(note) => ({ note: note || null })}
                  onSaved={refresh}
                />
                <MessageForm
                  label="Why you don't approve"
                  button="Decline"
                  path={`/api/v1/portal/owner/maintenance/${r.id}/decline`}
                  body={(reason) => ({ reason })}
                  onSaved={refresh}
                />
              </div>
            </Card>
          )
        }
      </RequestDetail>
    )
  }

  const data = home.data
  const waiting = requests.data?.filter((r) => r.awaitingOwnerApproval) ?? []
  return (
    <div className="space-y-6">
      <ErrorBox error={home.error ?? requests.error} />
      {data && (
        <>
          <div>
            <h1 className="text-xl font-semibold">Welcome, {data.ownerName}</h1>
            <p className="text-sm text-slate-600">Your properties managed by {data.organizationName}.</p>
          </div>
          <div className="grid gap-4 sm:grid-cols-3">
            <Stat label="Properties" value={data.properties.length} />
            <Stat label="Open repairs" value={data.openMaintenance} />
            <Stat label="Waiting for your approval" value={data.awaitingYourApproval} highlight={data.awaitingYourApproval > 0} />
          </div>
        </>
      )}
      {waiting.length > 0 && (
        <Card title="Repairs waiting for your approval">
          <RequestList items={waiting} onOpen={setOpen} empty="" />
        </Card>
      )}
      {data?.properties.length === 0 && <Empty>No properties are linked to you yet. Ask {data.organizationName} if this looks wrong.</Empty>}
      {data?.properties.map((p) => (
        <Card key={p.id} title={`${p.name}${p.sharePercent < 100 ? ` · your share ${p.sharePercent}%` : ''}`}>
          <p className="mb-3 text-sm text-slate-600">{[p.addressLine, p.city, p.state].filter(Boolean).join(', ')}</p>
          {p.units.length === 0 && <p className="text-sm text-slate-500">No units yet.</p>}
          <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
            {p.units.map((u) => (
              <li key={u.code} className="flex flex-wrap items-center justify-between gap-2 px-5 py-3 text-sm">
                <span className="min-w-0">
                  <span className="font-medium">Unit {u.code}</span>
                  <span className="text-slate-500"> · {enumLabel(u.type)}{u.bedrooms ? `, ${u.bedrooms} bed` : ''}</span>
                  {u.lease && (
                    <span className="block text-slate-600">
                      {u.lease.tenantName ?? 'Let'} · {formatNaira(u.lease.rentAmount)} {enumLabel(u.lease.billingFrequency).toLowerCase()} · until{' '}
                      {formatDay(u.lease.endDate)}
                    </span>
                  )}
                </span>
                <span className="flex gap-2">
                  {u.lease && (u.lease.stage === 'Expiring' || u.lease.stage === 'HoldingOver') && (
                    <Badge tone={statusTone(u.lease.stage)}>{enumLabel(u.lease.stage)}</Badge>
                  )}
                  <Badge tone={statusTone(u.occupancyStatus)}>{u.occupancyStatus}</Badge>
                </span>
              </li>
            ))}
          </ul>
        </Card>
      ))}
      {requests.data && (
        <Card title="Repairs on your properties">
          <RequestList items={requests.data} onOpen={setOpen} empty="No repairs reported." />
        </Card>
      )}
      <p className="text-xs text-slate-500">Statements and payouts will appear here once rent collection is live.</p>
    </div>
  )
}

function Stat({ label, value, highlight = false }: { label: string; value: number; highlight?: boolean }) {
  return (
    <div className={`rounded-lg p-4 ring-1 ${highlight ? 'bg-amber-50 ring-amber-200' : 'bg-white ring-slate-200'}`}>
      <p className="text-2xl font-semibold">{value}</p>
      <p className="text-sm text-slate-600">{label}</p>
    </div>
  )
}
