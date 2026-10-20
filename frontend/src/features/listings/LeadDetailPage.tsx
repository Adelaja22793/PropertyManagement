import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { LeadDetail, ListingAgent, Viewing, ViewingStatus } from '../../lib/types'
import { LeadStatuses } from '../../lib/types'
import { useSession } from '../../app/session'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, optionalText } from '../../lib/validation'
import { Badge, Button, Card, DetailList, Empty, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { lagosInputToUtc } from './listingFormat'

/** One enquiry: who asked, where it stands, and its viewings. */
export function LeadDetailPage() {
  const { id = '' } = useParams()
  const lead = useQuery({ queryKey: ['leads', id], queryFn: () => get<LeadDetail>(`/api/v1/leads/${id}`) })
  const l = lead.data
  if (!l) return <ErrorBox error={lead.error} />
  const open = l.status !== 'Won' && l.status !== 'Lost'

  return (
    <>
      <Link to={`/app/listings/${l.listingId}`} className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
        ← {l.listingHeadline}
      </Link>
      <PageHeader
        title={`${l.name} · ${l.reference}`}
        description={`${l.propertyName}, unit ${l.unitCode} · ${enumLabel(l.source)} enquiry, ${formatDate(l.createdAtUtc)}`}
      />
      <div className="space-y-6">
        <Card title="Enquiry">
          <DetailList
            items={[
              { label: 'Status', value: <Badge tone={statusTone(l.status)}>{enumLabel(l.status)}</Badge> },
              {
                label: 'Phone',
                value: l.phone && (
                  <a href={`tel:${l.phone.replace(/\s/g, '')}`} className="text-brand-700 hover:underline">
                    {l.phone}
                  </a>
                ),
              },
              {
                label: 'Email',
                value: l.email && (
                  <a href={`mailto:${l.email}`} className="text-brand-700 hover:underline">
                    {l.email}
                  </a>
                ),
              },
              { label: 'Notes', value: l.notes },
            ]}
          />
          {l.message && <p className="mt-4 rounded-md bg-slate-50 p-3 text-sm whitespace-pre-line text-slate-700">{l.message}</p>}
          <StatusForm key={l.updatedAtUtc} lead={l} />
        </Card>
        <Card title="Viewings">
          {l.viewings.length === 0 && <Empty>No viewings booked yet.</Empty>}
          <ul className="-mx-5 divide-y divide-slate-100">
            {l.viewings.map((v) => (
              <ViewingRow key={v.id} leadId={l.id} viewing={v} />
            ))}
          </ul>
          {open && <ScheduleViewing leadId={l.id} />}
        </Card>
      </div>
    </>
  )
}

function useSaveLead(leadId: string) {
  const queryClient = useQueryClient()
  return (saved: LeadDetail) => {
    queryClient.setQueryData(['leads', leadId], saved)
    return queryClient.invalidateQueries({ queryKey: ['leads', 'list'] })
  }
}

const statusSchema = z.object({ status: z.enum(LeadStatuses), notes: optionalText(4000) })
type StatusValues = z.infer<typeof statusSchema>

function StatusForm({ lead }: { lead: LeadDetail }) {
  const onSaved = useSaveLead(lead.id)
  const { register, handleSubmit } = useForm<StatusValues>({
    resolver: zodResolver(statusSchema),
    defaultValues: { status: lead.status, notes: lead.notes ?? '' },
  })
  const save = useMutation({
    mutationFn: (v: StatusValues) => put<LeadDetail>(`/api/v1/leads/${lead.id}/status`, { status: v.status, notes: v.notes.trim() }),
    onSuccess: onSaved,
  })
  return (
    <form onSubmit={handleSubmit((v) => save.mutate(v))} className="mt-5 space-y-3 border-t border-slate-100 pt-4">
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-[12rem_minmax(0,1fr)]">
        <SelectField label="Move to" {...register('status')}>
          {LeadStatuses.map((s) => (
            <option key={s} value={s}>
              {enumLabel(s)}
            </option>
          ))}
        </SelectField>
        <TextAreaField label="Notes" rows={2} {...register('notes')} />
      </div>
      <Button type="submit" variant="secondary" disabled={save.isPending}>
        {save.isSuccess && !save.isPending ? 'Saved' : 'Update enquiry'}
      </Button>
    </form>
  )
}

const outcomes: { value: Exclude<ViewingStatus, 'Scheduled'>; label: string }[] = [
  { value: 'Completed', label: 'Viewed' },
  { value: 'NoShow', label: 'No-show' },
  { value: 'Cancelled', label: 'Cancelled' },
]

function ViewingRow({ leadId, viewing }: { leadId: string; viewing: Viewing }) {
  const onSaved = useSaveLead(leadId)
  const [recording, setRecording] = useState(false)
  const [status, setStatus] = useState<Exclude<ViewingStatus, 'Scheduled'>>('Completed')
  const [outcome, setOutcome] = useState('')
  const record = useMutation({
    mutationFn: () => post<LeadDetail>(`/api/v1/leads/${leadId}/viewings/${viewing.id}/outcome`, { status, outcome: blankToNull(outcome) }),
    onSuccess: async (saved) => {
      await onSaved(saved)
      setRecording(false)
    },
  })
  return (
    <li className="px-5 py-3 text-sm">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="font-medium text-slate-900">{formatDate(viewing.scheduledAtUtc)}</p>
          <p className="text-slate-600">{[viewing.agentName, viewing.notes].filter(Boolean).join(' · ') || 'No agent named'}</p>
          {viewing.outcome && <p className="text-xs text-slate-500">{viewing.outcome}</p>}
        </div>
        <div className="flex items-center gap-2">
          <Badge tone={statusTone(viewing.status)}>{enumLabel(viewing.status)}</Badge>
          {viewing.status === 'Scheduled' && !recording && (
            <Button variant="ghost" onClick={() => setRecording(true)}>
              Record outcome
            </Button>
          )}
        </div>
      </div>
      {recording && (
        <div className="mt-3 space-y-3 rounded-md bg-slate-50 p-3 ring-1 ring-slate-200">
          <ErrorBox error={record.error} />
          <div className="grid gap-3 sm:grid-cols-[10rem_minmax(0,1fr)]">
            <SelectField label="How it went" value={status} onChange={(e) => setStatus(e.target.value as typeof status)}>
              {outcomes.map((o) => (
                <option key={o.value} value={o.value}>
                  {o.label}
                </option>
              ))}
            </SelectField>
            <TextField label="Outcome notes" value={outcome} onChange={(e) => setOutcome(e.target.value)} maxLength={1000} />
          </div>
          <div className="flex gap-2">
            <Button onClick={() => record.mutate()} disabled={record.isPending}>
              Save outcome
            </Button>
            <Button variant="secondary" onClick={() => setRecording(false)}>
              Cancel
            </Button>
          </div>
        </div>
      )}
    </li>
  )
}

const viewingSchema = z.object({
  at: z.string().min(1, 'Choose the date and time.'),
  agentUserId: z.string(),
  notes: optionalText(1000),
})
type ViewingValues = z.infer<typeof viewingSchema>

function ScheduleViewing({ leadId }: { leadId: string }) {
  const onSaved = useSaveLead(leadId)
  const { me } = useSession()
  const [open, setOpen] = useState(false)
  const agents = useQuery({
    queryKey: ['listings', 'agents'],
    queryFn: () => get<ListingAgent[]>('/api/v1/listings/agents'),
    enabled: open,
  })
  const { register, handleSubmit, formState, reset } = useForm<ViewingValues>({
    resolver: zodResolver(viewingSchema),
    defaultValues: { at: '', agentUserId: '', notes: '' },
  })
  const save = useMutation({
    mutationFn: (v: ViewingValues) =>
      post<LeadDetail>(`/api/v1/leads/${leadId}/viewings`, {
        scheduledAtUtc: lagosInputToUtc(v.at),
        agentUserId: v.agentUserId || null,
        notes: blankToNull(v.notes),
      }),
    onSuccess: async (saved) => {
      await onSaved(saved)
      reset()
      setOpen(false)
    },
  })
  if (!open)
    return (
      <div className="mt-4">
        <Button variant="secondary" onClick={() => setOpen(true)}>
          Book a viewing
        </Button>
      </div>
    )
  return (
    <form
      onSubmit={handleSubmit((v) => save.mutate(v))}
      noValidate
      className="mt-4 space-y-3 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200"
    >
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label="Date and time (Lagos)" type="datetime-local" {...register('at')} error={formState.errors.at?.message} />
        <SelectField label="Agent" {...register('agentUserId')}>
          <option value="">{me ? `Me (${me.fullName})` : 'Me'}</option>
          {agents.data
            ?.filter((a) => a.userId !== me?.userId)
            .map((a) => (
              <option key={a.userId} value={a.userId}>
                {a.fullName}
              </option>
            ))}
        </SelectField>
        <div className="sm:col-span-2">
          <TextField id="viewing-notes" label="Viewing notes" placeholder="Meet at the gate" {...register('notes')} error={formState.errors.notes?.message} />
        </div>
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Book viewing
        </Button>
        <Button type="button" variant="secondary" onClick={() => setOpen(false)}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
