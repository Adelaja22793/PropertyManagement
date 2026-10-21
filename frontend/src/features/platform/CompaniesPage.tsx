import { useState, type FormEvent } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { Plan, PlatformOrganization } from '../../lib/types'
import { useSession } from '../../app/session'
import { Forbidden } from '../../app/guards'
import { formatDate } from '../../lib/format'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, SearchInput, SelectField, TextField } from '../../components/ui'
import { subscriptionStatusLabels, subscriptionTone } from '../subscription/subscription'

const companiesKey = ['platform-companies']

/** Platform staff manage companies' trials, paid-up dates, plans, suspension and cancellation. No company records. */
export function CompaniesPage() {
  const { me } = useSession()
  const [search, setSearch] = useState('')
  const [open, setOpen] = useState<string | null>(null)
  const allowed = !!me?.isPlatformAdmin && me.twoFactorEnabled
  const companies = useQuery({
    queryKey: [...companiesKey, search],
    queryFn: () => get<PlatformOrganization[]>(`/api/v1/platform/organizations?search=${encodeURIComponent(search)}`),
    enabled: allowed,
  })
  if (!me?.isPlatformAdmin) return <Forbidden />

  return (
    <>
      <PageHeader
        title="Companies"
        description="Trials, plans and subscriptions. Every change goes in the company's audit log. You can't see their records from here."
      />
      {!me.twoFactorEnabled ? (
        <Card>
          <p className="text-sm text-slate-700">
            Turn on two-step sign-in before managing companies.{' '}
            <Link to="/app/security" className="font-medium text-brand-700 underline">
              Set it up
            </Link>
          </p>
        </Card>
      ) : (
        <Card>
          <div className="mb-4 max-w-sm">
            <SearchInput label="Search companies" value={search} onChange={setSearch} />
          </div>
          <ErrorBox error={companies.error} />
          {companies.data?.length === 0 && <Empty>No companies found.</Empty>}
          <ul className="divide-y divide-slate-100">
            {companies.data?.map((c) => (
              <li key={c.id} className="py-3">
                <div className="flex flex-wrap items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
                      {c.name}
                      <Badge tone={subscriptionTone(c.subscription.status)}>{subscriptionStatusLabels[c.subscription.status]}</Badge>
                    </p>
                    <p className="text-xs text-slate-500">
                      {c.slug} · {c.planName} · {c.units} of {c.maxUnits} units · {c.staff} of {c.maxUsers} staff · {timing(c)}
                    </p>
                  </div>
                  <Button variant="secondary" onClick={() => setOpen(open === c.id ? null : c.id)} aria-expanded={open === c.id}>
                    {open === c.id ? 'Close' : `Manage ${c.name}`}
                  </Button>
                </div>
                {open === c.id && <ManageCompany company={c} />}
              </li>
            ))}
          </ul>
        </Card>
      )}
    </>
  )
}

function timing(c: PlatformOrganization): string {
  const s = c.subscription
  if (s.status === 'Trial' && s.trialEndsAtUtc) return `trial ends ${formatDate(s.trialEndsAtUtc)}`
  if (s.status === 'Active' && s.currentPeriodEndsAtUtc) return `paid up to ${formatDate(s.currentPeriodEndsAtUtc)}`
  if (s.status === 'PastDue' && s.suspendsAtUtc) return `read-only from ${formatDate(s.suspendsAtUtc)}`
  if (s.status === 'Cancelled' && s.exportUntilUtc) return `export open until ${formatDate(s.exportUntilUtc)}`
  return `joined ${formatDate(c.createdAtUtc)}`
}

/** A date input's value, as the end of that day in Lagos. */
const endOfDay = (date: string) => `${date}T23:59:59+01:00`

function ManageCompany({ company }: { company: PlatformOrganization }) {
  const queryClient = useQueryClient()
  const plans = useQuery({ queryKey: ['plans'], queryFn: () => get<Plan[]>('/api/v1/plans') })
  const [trialUntil, setTrialUntil] = useState('')
  const [paidUntil, setPaidUntil] = useState('')
  const [planCode, setPlanCode] = useState(company.planCode)
  const [reason, setReason] = useState('')
  const base = `/api/v1/platform/organizations/${company.id}`
  const act = useMutation({
    mutationFn: ({ path, body }: { path: string; body: unknown }) => post<PlatformOrganization>(`${base}/${path}`, body),
    onSuccess: async () => {
      setReason('')
      await queryClient.invalidateQueries({ queryKey: companiesKey })
    },
  })
  const cancelled = company.subscription.status === 'Cancelled'
  const submit = (path: string, body: unknown) => (e: FormEvent) => {
    e.preventDefault()
    act.mutate({ path, body })
  }

  return (
    <div className="mt-3 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200">
      <ErrorBox error={act.error ?? plans.error} />
      {!cancelled && (
        <form className="flex flex-wrap items-end gap-3" onSubmit={submit('extend-trial', { until: endOfDay(trialUntil) })}>
          <div className="w-48">
            <TextField label="Trial until" type="date" value={trialUntil} onChange={(e) => setTrialUntil(e.target.value)} required />
          </div>
          <Button type="submit" variant="secondary" disabled={!trialUntil || act.isPending}>
            Extend trial
          </Button>
        </form>
      )}
      <form className="flex flex-wrap items-end gap-3" onSubmit={submit('activate', { planCode, until: endOfDay(paidUntil) })}>
        <div className="w-40">
          <SelectField label="Plan" value={planCode} onChange={(e) => setPlanCode(e.target.value)}>
            {plans.data?.map((p) => (
              <option key={p.code} value={p.code}>
                {p.name}
              </option>
            ))}
          </SelectField>
        </div>
        <div className="w-48">
          <TextField label="Paid up to" type="date" value={paidUntil} onChange={(e) => setPaidUntil(e.target.value)} required />
        </div>
        <Button type="submit" variant="secondary" disabled={!paidUntil || act.isPending}>
          {cancelled ? 'Reinstate' : 'Record paid-up date'}
        </Button>
        {!cancelled && (
          <Button
            type="button"
            variant="ghost"
            disabled={planCode === company.planCode || act.isPending}
            onClick={() => act.mutate({ path: 'plan', body: { planCode } })}
          >
            Change plan only
          </Button>
        )}
      </form>
      {!cancelled && (
        <div className="flex flex-wrap items-end gap-3 border-t border-slate-200 pt-4">
          <div className="min-w-[16rem] flex-1">
            <TextField label="Reason (goes in their audit log)" value={reason} onChange={(e) => setReason(e.target.value)} />
          </div>
          {company.subscription.status !== 'Suspended' && (
            <Button
              variant="secondary"
              disabled={!reason.trim() || act.isPending}
              onClick={() => act.mutate({ path: 'suspend', body: { reason } })}
            >
              Make read-only
            </Button>
          )}
          <Button
            variant="danger"
            disabled={!reason.trim() || act.isPending}
            onClick={() => {
              if (confirm(`Cancel ${company.name}? Their team loses access; admins can download their data for 90 days.`))
                act.mutate({ path: 'cancel', body: { reason } })
            }}
          >
            Cancel subscription
          </Button>
        </div>
      )}
    </div>
  )
}
