import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { PortalLease, PortalRequestDetail, PortalRequestSummary, TenantPortal as TenantPortalData } from '../../lib/types'
import { MaintenanceCategories, MaintenanceUrgencies } from '../../lib/types'
import { enumLabel, formatDay, formatNaira } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, optionalText } from '../../lib/validation'
import { Badge, Button, Card, DetailList, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'
import { MessageForm, RequestDetail, RequestList } from './PortalRequests'
import { useOpenRequest } from './useOpenRequest'

/** What a tenant sees: their lease, notices served on it, and their repair requests. */
export function TenantPortal() {
  const [open, setOpen] = useOpenRequest()
  const [reporting, setReporting] = useState(false)
  const home = useQuery({ queryKey: ['portal', 'tenant'], queryFn: () => get<TenantPortalData>('/api/v1/portal/tenant') })
  const requests = useQuery({
    queryKey: ['portal', 'tenant', 'maintenance'],
    queryFn: () => get<PortalRequestSummary[]>('/api/v1/portal/tenant/maintenance'),
  })

  if (open) {
    return (
      <RequestDetail path={`/api/v1/portal/tenant/maintenance/${open}`} onBack={() => setOpen(null)}>
        {(r, refresh) =>
          !['Completed', 'Rejected', 'Cancelled'].includes(r.status) && (
            <Card>
              <MessageForm
                label="Add more details"
                button="Send"
                path={`/api/v1/portal/tenant/maintenance/${r.id}/comments`}
                body={(text) => ({ text })}
                onSaved={refresh}
              />
            </Card>
          )
        }
      </RequestDetail>
    )
  }

  const data = home.data
  const current = data?.leases.filter((l) => l.status === 'Active') ?? []
  return (
    <div className="space-y-6">
      <ErrorBox error={home.error ?? requests.error} />
      {data && (
        <div>
          <h1 className="text-xl font-semibold">Welcome, {data.tenantName}</h1>
          <p className="text-sm text-slate-600">Your tenancy with {data.organizationName}.</p>
        </div>
      )}
      {data?.leases.length === 0 && <p className="text-sm text-slate-600">No lease is linked to you yet.</p>}
      {data?.leases.map((l) => <LeaseCard key={l.id} lease={l} />)}

      <Card
        title="Repairs"
        actions={
          current.length > 0 &&
          !reporting && (
            <Button variant="secondary" onClick={() => setReporting(true)}>
              Report a repair
            </Button>
          )
        }
      >
        {reporting && (
          <ReportForm
            leases={current}
            onDone={(saved) => {
              setReporting(false)
              if (saved) setOpen(saved.id)
            }}
          />
        )}
        {requests.data && <RequestList items={requests.data} onOpen={setOpen} empty="You haven't reported any repairs." />}
      </Card>
      <p className="text-xs text-slate-500">Invoices, receipts and online payment will appear here once rent collection is live.</p>
    </div>
  )
}

function LeaseCard({ lease: l }: { lease: PortalLease }) {
  return (
    <Card title={`${l.propertyName}, unit ${l.unitCode}`}>
      <div className="mb-4">
        <Badge tone={statusTone(l.stage)}>{enumLabel(l.stage)}</Badge>
      </div>
      <DetailList
        items={[
          { label: 'Address', value: [l.addressLine, l.city, l.state].filter(Boolean).join(', ') },
          { label: 'Lease', value: l.reference },
          { label: 'Term', value: `${formatDay(l.startDate)} to ${formatDay(l.endDate)}` },
          { label: 'Rent', value: `${formatNaira(l.rentAmount)} ${enumLabel(l.billingFrequency).toLowerCase()}` },
          { label: 'Rent due', value: l.dueDaysBeforePeriod === 0 ? 'On the first day of each period' : `${l.dueDaysBeforePeriod} days before each period` },
          { label: 'Notice period', value: l.noticePeriodDays === null ? null : `${l.noticePeriodDays} days` },
          { label: 'Tenants', value: l.tenants.join(', ') },
          { label: 'Renewal terms', value: l.renewalTerms },
          ...(l.moveOutDate ? [{ label: 'Moved out', value: formatDay(l.moveOutDate) }] : []),
        ]}
      />
      {l.charges.length > 0 && (
        <>
          <h3 className="mt-4 text-xs font-medium text-slate-500">Other charges</h3>
          <ul className="mt-1 space-y-1 text-sm">
            {l.charges.map((c, i) => (
              <li key={i} className="flex justify-between gap-4">
                <span>
                  {enumLabel(c.type)}
                  {c.description && <span className="text-slate-500"> · {c.description}</span>}
                </span>
                <span className="tabular-nums">{formatNaira(c.amount)}</span>
              </li>
            ))}
          </ul>
        </>
      )}
      {l.notices.length > 0 && (
        <>
          <h3 className="mt-4 text-xs font-medium text-slate-500">Notices served on you</h3>
          <ul className="mt-1 space-y-1 text-sm">
            {l.notices.map((n, i) => (
              <li key={i}>
                {enumLabel(n.type)}, served {formatDay(n.servedOn)}, expires {formatDay(n.expiresOn)}
                {n.notes && <span className="block text-xs text-slate-500">{n.notes}</span>}
              </li>
            ))}
          </ul>
        </>
      )}
    </Card>
  )
}

const schema = z.object({
  leaseId: z.string().min(1),
  title: z.string().trim().min(3, 'Say what is wrong in a few words.').max(200),
  description: optionalText(4000),
  category: z.enum(MaintenanceCategories),
  urgency: z.enum(MaintenanceUrgencies),
})
type Values = z.infer<typeof schema>

function ReportForm({ leases, onDone }: { leases: PortalLease[]; onDone: (saved?: PortalRequestDetail) => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { leaseId: leases[0].id, title: '', description: '', category: 'Plumbing', urgency: 'Normal' },
  })
  const save = useMutation({
    mutationFn: (v: Values) =>
      post<PortalRequestDetail>('/api/v1/portal/tenant/maintenance', { ...v, title: v.title.trim(), description: blankToNull(v.description) }),
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['portal'] })
      onDone(saved)
    },
  })
  const errors = formState.errors
  return (
    <form className="mb-6 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        {leases.length > 1 && (
          <SelectField label="Home" {...register('leaseId')}>
            {leases.map((l) => (
              <option key={l.id} value={l.id}>
                {l.propertyName}, unit {l.unitCode}
              </option>
            ))}
          </SelectField>
        )}
        <TextField label="What's wrong?" placeholder="Bathroom tap leaking" {...register('title')} error={errors.title?.message} />
        <SelectField label="Type of problem" {...register('category')}>
          {MaintenanceCategories.map((c) => (
            <option key={c} value={c}>
              {enumLabel(c)}
            </option>
          ))}
        </SelectField>
        <SelectField label="How urgent?" hint="Emergency means a danger to people or the building, such as flooding or a gas smell." {...register('urgency')}>
          {MaintenanceUrgencies.map((u) => (
            <option key={u} value={u}>
              {u}
            </option>
          ))}
        </SelectField>
      </div>
      <TextAreaField label="More details" rows={3} {...register('description')} error={errors.description?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Send request
        </Button>
        <Button type="button" variant="secondary" onClick={() => onDone()}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
