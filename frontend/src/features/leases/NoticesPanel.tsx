import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { LeaseNotice, NoticeDefault } from '../../lib/types'
import { NoticeTypes, ServiceMethods } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDay, todayInLagos } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, optionalText } from '../../lib/validation'
import { Badge, Button, Card, Empty, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

/** Notices served on the lease's tenants: what, when, how, and when each runs out. */
export function NoticesPanel({ leaseId, canServe }: { leaseId: string; canServe: boolean }) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const allowed = canServe && can(Permissions.leasesIssueNotice)
  const [serving, setServing] = useState(false)
  const notices = useQuery({
    queryKey: ['leases', leaseId, 'notices'],
    queryFn: () => get<LeaseNotice[]>(`/api/v1/leases/${leaseId}/notices`),
  })
  const withdraw = useMutation({
    mutationFn: ({ notice, reason }: { notice: LeaseNotice; reason: string }) =>
      post<LeaseNotice>(`/api/v1/leases/${leaseId}/notices/${notice.id}/withdraw`, { reason: blankToNull(reason) }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['leases', leaseId, 'notices'] }),
  })

  return (
    <Card
      title="Notices"
      actions={
        allowed &&
        !serving && (
          <Button variant="secondary" onClick={() => setServing(true)}>
            Record a notice
          </Button>
        )
      }
    >
      {serving && <ServeNoticeForm leaseId={leaseId} onDone={() => setServing(false)} />}
      <ErrorBox error={notices.error ?? withdraw.error} />
      {notices.data?.length === 0 && <Empty>No notices served.</Empty>}
      <ul className="divide-y divide-slate-100">
        {notices.data?.map((n) => (
          <li key={n.id} className="flex flex-wrap items-start justify-between gap-3 py-3 text-sm">
            <div className="min-w-0">
              <p className="font-medium">{enumLabel(n.type)}</p>
              <p className="text-slate-600">
                Served {formatDay(n.servedOn)} · {enumLabel(n.method)}
                {n.servedBy && ` by ${n.servedBy}`}
              </p>
              <p className="text-slate-600">
                {n.status === 'Withdrawn'
                  ? `Withdrawn${n.withdrawnReason ? `: ${n.withdrawnReason}` : ''}`
                  : n.hasExpired
                    ? `Expired ${formatDay(n.expiresOn)}`
                    : `Expires ${formatDay(n.expiresOn)} (${n.daysLeft} ${n.daysLeft === 1 ? 'day' : 'days'} left)`}
              </p>
              {n.notes && <p className="text-xs text-slate-500">{n.notes}</p>}
            </div>
            <div className="flex items-center gap-2">
              <Badge tone={n.status === 'Served' && n.hasExpired ? 'red' : statusTone(n.status)}>
                {n.status === 'Served' && n.hasExpired ? 'Expired' : n.status}
              </Badge>
              {allowed && n.status === 'Served' && (
                <Button
                  variant="ghost"
                  aria-label={`Withdraw ${enumLabel(n.type).toLowerCase()} served ${formatDay(n.servedOn)}`}
                  onClick={() => {
                    const reason = prompt('Why is this notice being withdrawn? (optional)')
                    if (reason !== null) withdraw.mutate({ notice: n, reason })
                  }}
                >
                  Withdraw
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>
      <p className="mt-3 text-xs text-slate-500">
        Notice periods default to the Lagos Tenancy Law 2011. Other states differ; have your lawyer confirm the wording and periods.
      </p>
    </Card>
  )
}

const schema = z
  .object({
    type: z.enum(NoticeTypes),
    servedOn: z.string().min(1, 'Enter the date it was served.'),
    method: z.enum(ServiceMethods),
    servedBy: optionalText(200),
    expiresOn: z.string(),
    notes: optionalText(2000),
  })
  .refine((v) => v.servedOn <= todayInLagos(), { path: ['servedOn'], message: "Record a notice once it's served." })
  .refine((v) => !v.expiresOn || v.expiresOn >= v.servedOn, { path: ['expiresOn'], message: "It can't expire before it's served." })
  .refine((v) => v.type !== 'Other' || v.notes !== '', { path: ['notes'], message: 'Describe the notice.' })
type Values = z.infer<typeof schema>

function ServeNoticeForm({ leaseId, onDone }: { leaseId: string; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { type: 'NoticeToQuit', servedOn: todayInLagos(), method: 'PersonalService', servedBy: '', expiresOn: '', notes: '' },
  })
  const [type, servedOn] = useWatch({ control, name: ['type', 'servedOn'] })
  const suggestion = useQuery({
    queryKey: ['leases', leaseId, 'notice-default', type, servedOn],
    queryFn: () => get<NoticeDefault>(`/api/v1/leases/${leaseId}/notices/default?type=${type}&servedOn=${servedOn}`),
    enabled: !!servedOn,
  })
  const serve = useMutation({
    mutationFn: (v: Values) =>
      post<LeaseNotice>(`/api/v1/leases/${leaseId}/notices`, {
        type: v.type,
        servedOn: v.servedOn,
        method: v.method,
        servedBy: blankToNull(v.servedBy),
        expiresOn: v.expiresOn || null,
        notes: blankToNull(v.notes),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['leases', leaseId, 'notices'] })
      onDone()
    },
  })
  const errors = formState.errors

  return (
    <form className="mb-6 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => serve.mutate(v))} noValidate>
      <h3 className="text-sm font-semibold">Record a notice that has been served</h3>
      <ErrorBox error={serve.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField label="Notice" {...register('type')}>
          {NoticeTypes.map((t) => (
            <option key={t} value={t}>
              {enumLabel(t)}
            </option>
          ))}
        </SelectField>
        <SelectField label="How it was served" {...register('method')}>
          {ServiceMethods.map((m) => (
            <option key={m} value={m}>
              {enumLabel(m)}
            </option>
          ))}
        </SelectField>
        <TextField label="Date served" type="date" {...register('servedOn')} error={errors.servedOn?.message} />
        <TextField label="Served by" placeholder="Lawyer, bailiff or staff member" {...register('servedBy')} error={errors.servedBy?.message} />
        <TextField
          label="Expires on"
          type="date"
          hint={suggestion.data ? `Leave blank for ${formatDay(suggestion.data.expiresOn)} (${suggestion.data.days} days). ${suggestion.data.basis}` : undefined}
          {...register('expiresOn')}
          error={errors.expiresOn?.message}
        />
      </div>
      <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={serve.isPending}>
          Save notice
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
