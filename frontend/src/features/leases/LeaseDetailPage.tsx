import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { LeaseDetail } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate, formatDay, formatNaira, todayInLagos } from '../../lib/format'
import { blankToNull, optionalText, requiredAmount } from '../../lib/validation'
import { Badge, Button, Card, DetailList, ErrorBox, PageHeader, TextAreaField, TextField } from '../../components/ui'
import { StageBadge } from './LeaseList'
import { NoticesPanel } from './NoticesPanel'
import { InspectionsPanel } from './InspectionsPanel'
import { defaultEndDate, expiryText, moveInTotal, nextDay, rentChangePercent } from './leaseMath'

type Panel = 'renew' | 'end' | 'cancel' | null

export function LeaseDetailPage() {
  const { id = '' } = useParams()
  // A fresh page per lease, so an open form never carries over to the renewal or back.
  return <LeaseDetailView key={id} id={id} />
}

function LeaseDetailView({ id }: { id: string }) {
  const { can } = useSession()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [panel, setPanel] = useState<Panel>(null)
  const lease = useQuery({ queryKey: ['leases', id], queryFn: () => get<LeaseDetail>(`/api/v1/leases/${id}`) })
  const refresh = () => Promise.all([queryClient.invalidateQueries({ queryKey: ['leases'] }), queryClient.invalidateQueries({ queryKey: ['properties'] })])
  const activate = useMutation({ mutationFn: () => post<LeaseDetail>(`/api/v1/leases/${id}/activate`), onSuccess: refresh })
  const l = lease.data

  const back = (
    <Link to="/app/leases" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
      ← Leases
    </Link>
  )
  if (!l) {
    return (
      <>
        {back}
        <ErrorBox error={lease.error} />
        {lease.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }

  const draft = l.status === 'Draft'
  const active = l.status === 'Active'
  const oneOff = l.charges.filter((c) => c.recurrence === 'OneOff')
  const recurring = l.charges.filter((c) => c.recurrence === 'EachPeriod')

  return (
    <>
      {back}
      <PageHeader
        title={`${l.tenants[0]?.name ?? 'Lease'} · unit ${l.unitCode}`}
        description={`${l.reference} · ${l.propertyName}`}
        actions={
          panel === null && (
            <div className="flex flex-wrap gap-2">
              {draft && can(Permissions.leasesCreate) && (
                <>
                  <Button onClick={() => activate.mutate()} disabled={activate.isPending}>
                    Activate lease
                  </Button>
                  <Button variant="secondary" onClick={() => navigate(`/app/leases/${l.id}/edit`)}>
                    Edit
                  </Button>
                  <Button variant="danger" onClick={() => setPanel('cancel')}>
                    Discard draft
                  </Button>
                </>
              )}
              {active && !l.renewalLease && can(Permissions.leasesRenew) && (
                <Button variant="secondary" onClick={() => setPanel('renew')}>
                  Renew
                </Button>
              )}
              {active && can(Permissions.leasesTerminate) && (
                <Button variant="danger" onClick={() => setPanel('end')}>
                  End lease
                </Button>
              )}
            </div>
          )
        }
      />
      <div className="space-y-6">
        <ErrorBox error={activate.error} />
        {panel === 'renew' && <RenewForm lease={l} onDone={() => setPanel(null)} />}
        {panel === 'end' && <EndForm lease={l} onDone={() => setPanel(null)} />}
        {panel === 'cancel' && <CancelForm lease={l} onDone={() => setPanel(null)} />}
        {draft && (
          <p className="rounded-md bg-slate-50 p-3 text-sm text-slate-700 ring-1 ring-slate-200">
            This lease is a draft. It doesn't affect the unit until you activate it, and its terms are fixed once active.
          </p>
        )}
        {l.stage === 'HoldingOver' && (
          <p className="rounded-md bg-red-50 p-3 text-sm text-red-800 ring-1 ring-red-200">
            The term ended on {formatDay(l.endDate)} and the tenant is still in the unit. Renew the lease or record the move-out.
          </p>
        )}
        <Card>
          <div className="mb-4 flex flex-wrap items-center gap-2">
            <StageBadge stage={l.stage} />
            {active && <span className="text-sm text-slate-600">{expiryText(l.daysToExpiry)}</span>}
          </div>
          <DetailList
            items={[
              { label: 'Property', value: <Link className="text-brand-700 hover:underline" to={`/app/properties/${l.propertyId}`}>{l.propertyName}</Link> },
              { label: 'Unit', value: l.unitCode },
              { label: 'Term', value: `${formatDay(l.startDate)} to ${formatDay(l.endDate)}` },
              { label: 'Rent', value: `${formatNaira(l.rentAmount)} ${enumLabel(l.billingFrequency).toLowerCase()}` },
              { label: 'Rent due', value: l.dueDaysBeforePeriod === 0 ? 'On the first day of each period' : `${l.dueDaysBeforePeriod} days before each period` },
              { label: 'Grace period', value: `${l.graceDays} days` },
              { label: 'Notice period', value: l.noticePeriodDays === null ? null : `${l.noticePeriodDays} days` },
              { label: 'Holdover rent', value: l.holdoverRentAmount === null ? null : formatNaira(l.holdoverRentAmount) },
              { label: 'Renewal terms', value: l.renewalTerms },
              { label: 'Activated', value: l.activatedAtUtc ? `${formatDate(l.activatedAtUtc)}${l.activatedByName ? ` by ${l.activatedByName}` : ''}` : null },
              ...(l.moveOutDate ? [{ label: 'Moved out', value: formatDay(l.moveOutDate) }] : []),
              ...(l.endReason ? [{ label: l.status === 'Cancelled' ? 'Reason discarded' : 'Reason', value: l.endReason }] : []),
              ...(l.notes ? [{ label: 'Notes', value: l.notes }] : []),
            ]}
          />
        </Card>

        {(l.previousLease || l.renewalLease) && (
          <Card title="Renewals">
            <ul className="space-y-2 text-sm">
              {l.previousLease && (
                <li>
                  Renews <Link className="font-medium text-brand-700 hover:underline" to={`/app/leases/${l.previousLease.id}`}>{l.previousLease.reference}</Link>{' '}
                  ({formatDay(l.previousLease.startDate)} to {formatDay(l.previousLease.endDate)}) at {formatNaira(l.previousLease.rentAmount)}.
                  Rent change: {rentChangePercent(l.previousLease.rentAmount, l.rentAmount)}%.
                </li>
              )}
              {l.renewalLease && (
                <li>
                  Renewed by <Link className="font-medium text-brand-700 hover:underline" to={`/app/leases/${l.renewalLease.id}`}>{l.renewalLease.reference}</Link>{' '}
                  from {formatDay(l.renewalLease.startDate)} at {formatNaira(l.renewalLease.rentAmount)} <Badge>{enumLabel(l.renewalLease.status)}</Badge>
                </li>
              )}
            </ul>
          </Card>
        )}

        <Card title="Tenants and guarantors">
          <ul className="divide-y divide-slate-100 text-sm">
            {l.tenants.map((t) => (
              <li key={t.tenantId} className="py-2">
                <Link className="font-medium text-brand-700 hover:underline" to={`/app/tenants/${t.tenantId}`}>{t.name}</Link>
                {t.isPrimary && <span className="ml-2 text-xs text-slate-500">Primary tenant</span>}
                <span className="block text-slate-600">{[t.phone, t.email].filter(Boolean).join(' · ')}</span>
              </li>
            ))}
          </ul>
          <h3 className="mt-4 text-xs font-medium text-slate-500">Guarantors</h3>
          {l.guarantors.length === 0 && <p className="mt-1 text-sm text-slate-500">None named on this lease.</p>}
          <ul className="mt-1 space-y-1 text-sm">
            {l.guarantors.map((g) => (
              <li key={g.guarantorId}>
                {g.fullName} {g.phone && <span className="text-slate-500">· {g.phone}</span>}{' '}
                {!g.signedFormReceived && <Badge tone="amber">Form not received</Badge>}
              </li>
            ))}
          </ul>
        </Card>

        {l.status !== 'Draft' && l.status !== 'Cancelled' && <NoticesPanel leaseId={l.id} canServe={active} />}
        {l.status !== 'Cancelled' && l.status !== 'Renewed' && <InspectionsPanel leaseId={l.id} status={l.status} />}

        <Card title="Charges">
          <table className="w-full text-left text-sm">
            <tbody className="divide-y divide-slate-100">
              <tr>
                <td className="py-2">Rent</td>
                <td className="py-2 text-slate-500">{enumLabel(l.billingFrequency)}</td>
                <td className="py-2 text-right tabular-nums">{formatNaira(l.rentAmount)}</td>
              </tr>
              {[...recurring, ...oneOff].map((c, i) => (
                <tr key={c.id ?? i}>
                  <td className="py-2">
                    {enumLabel(c.type)}
                    {c.description && <span className="block text-xs text-slate-500">{c.description}</span>}
                  </td>
                  <td className="py-2 text-slate-500">{enumLabel(c.recurrence)}</td>
                  <td className="py-2 text-right tabular-nums">{formatNaira(c.amount)}</td>
                </tr>
              ))}
              <tr className="font-semibold">
                <td className="py-2" colSpan={2}>
                  Move-in total
                </td>
                <td className="py-2 text-right tabular-nums">{formatNaira(moveInTotal(l.rentAmount, l.charges))}</td>
              </tr>
            </tbody>
          </table>
          <p className="mt-2 text-xs text-slate-500">Recorded as terms only. Invoices and payments arrive once the finance design is signed off.</p>
        </Card>
      </div>
    </>
  )
}

const renewSchema = z
  .object({
    startDate: z.string().min(1, 'Enter the start date.'),
    endDate: z.string().min(1, 'Enter the end date.'),
    rentAmount: requiredAmount.refine((v) => Number(v) > 0, 'Enter the new rent.'),
    notes: optionalText(2000),
  })
  .refine((v) => v.endDate > v.startDate, { path: ['endDate'], message: 'The end date must be after the start date.' })

function RenewForm({ lease, onDone }: { lease: LeaseDetail; onDone: () => void }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const start = nextDay(lease.endDate)
  const { register, handleSubmit, formState, control } = useForm<z.infer<typeof renewSchema>>({
    resolver: zodResolver(renewSchema),
    defaultValues: { startDate: start, endDate: defaultEndDate(start), rentAmount: lease.rentAmount.toString(), notes: '' },
  })
  const rent = useWatch({ control, name: 'rentAmount' })
  const renew = useMutation({
    mutationFn: (v: z.infer<typeof renewSchema>) =>
      post<LeaseDetail>(`/api/v1/leases/${lease.id}/renew`, {
        startDate: v.startDate,
        endDate: v.endDate,
        rentAmount: Number(v.rentAmount),
        notes: blankToNull(v.notes),
      }),
    onSuccess: async (renewal) => {
      await queryClient.invalidateQueries({ queryKey: ['leases'] })
      navigate(`/app/leases/${renewal.id}`)
    },
  })
  const change = Number(rent) > 0 ? rentChangePercent(lease.rentAmount, Number(rent)) : null

  return (
    <Card title="Renew this lease">
      <form className="space-y-4" onSubmit={handleSubmit((v) => renew.mutate(v))} noValidate>
        <p className="text-sm text-slate-600">
          This drafts the next lease for the same unit, tenants and guarantors. Service charge carries over; move-in charges don't.
          It takes effect when you activate it.
        </p>
        <ErrorBox error={renew.error} />
        <div className="grid gap-4 sm:grid-cols-3">
          <TextField label="New start date" type="date" {...register('startDate')} error={formState.errors.startDate?.message} />
          <TextField label="New end date" type="date" {...register('endDate')} error={formState.errors.endDate?.message} />
          <TextField
            label="New rent per period (₦)"
            inputMode="decimal"
            hint={change === null ? undefined : `${formatNaira(Number(rent))} · ${change > 0 ? '+' : ''}${change}% on current rent`}
            {...register('rentAmount')}
            error={formState.errors.rentAmount?.message}
          />
        </div>
        <TextAreaField label="Rent review notes" rows={2} {...register('notes')} error={formState.errors.notes?.message} />
        <div className="flex gap-2">
          <Button type="submit" disabled={renew.isPending}>
            Draft renewal
          </Button>
          <Button type="button" variant="secondary" onClick={onDone}>
            Cancel
          </Button>
        </div>
      </form>
    </Card>
  )
}

const endSchema = z.object({ moveOutDate: z.string().min(1, 'Enter the move-out date.'), reason: optionalText(500) })

function EndForm({ lease, onDone }: { lease: LeaseDetail; onDone: () => void }) {
  const queryClient = useQueryClient()
  const today = todayInLagos()
  const { register, handleSubmit, formState, control } = useForm<z.infer<typeof endSchema>>({
    resolver: zodResolver(endSchema),
    defaultValues: { moveOutDate: today < lease.endDate ? today : lease.endDate, reason: '' },
  })
  const moveOut = useWatch({ control, name: 'moveOutDate' })
  const early = moveOut !== '' && moveOut < lease.endDate
  const end = useMutation({
    mutationFn: (v: z.infer<typeof endSchema>) =>
      post<LeaseDetail>(`/api/v1/leases/${lease.id}/end`, { moveOutDate: v.moveOutDate, reason: blankToNull(v.reason) }),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['leases'] }), queryClient.invalidateQueries({ queryKey: ['properties'] })])
      onDone()
    },
  })

  return (
    <Card title="End this lease">
      <form className="space-y-4" onSubmit={handleSubmit((v) => end.mutate(v))} noValidate>
        <p className="text-sm text-slate-600">
          Record the day the tenant handed the unit back. The unit is free to let from that day. Nothing is deleted.
        </p>
        <ErrorBox error={end.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Move-out date"
            type="date"
            hint={early ? `Before the end date (${formatDay(lease.endDate)}), so this is an early termination.` : undefined}
            {...register('moveOutDate')}
            error={formState.errors.moveOutDate?.message}
          />
          <TextField label={early ? 'Reason (required)' : 'Reason (optional)'} {...register('reason')} error={formState.errors.reason?.message} />
        </div>
        <div className="flex gap-2">
          <Button type="submit" variant="danger" disabled={end.isPending}>
            {early ? 'Terminate lease' : 'End lease'}
          </Button>
          <Button type="button" variant="secondary" onClick={onDone}>
            Cancel
          </Button>
        </div>
      </form>
    </Card>
  )
}

function CancelForm({ lease, onDone }: { lease: LeaseDetail; onDone: () => void }) {
  const queryClient = useQueryClient()
  const [reason, setReason] = useState('')
  const cancel = useMutation({
    mutationFn: () => post<LeaseDetail>(`/api/v1/leases/${lease.id}/cancel`, { reason: blankToNull(reason) }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['leases'] })
      onDone()
    },
  })
  return (
    <Card title="Discard this draft">
      <div className="space-y-4">
        <ErrorBox error={cancel.error} />
        <TextField label="Reason (optional)" name="cancel-reason" value={reason} onChange={(e) => setReason(e.target.value)} />
        <div className="flex gap-2">
          <Button variant="danger" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
            Discard draft
          </Button>
          <Button variant="secondary" onClick={onDone}>
            Keep it
          </Button>
        </div>
      </div>
    </Card>
  )
}
