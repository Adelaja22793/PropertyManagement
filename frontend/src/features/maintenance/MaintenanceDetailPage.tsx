import { useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { Assignee, MaintenanceDetail, MaintenanceUpdate, Paged, Vendor } from '../../lib/types'
import { enumLabel, formatDate, formatDay, formatNaira, todayInLagos } from '../../lib/format'
import { blankToNull, numberOrNull, optionalAmount, optionalText, requiredAmount } from '../../lib/validation'
import { Button, Card, DetailList, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { StatusBadge, UrgencyBadge } from './MaintenanceList'
import { MaintenanceForm } from './MaintenanceForm'
import { DocumentsPanel } from '../documents/DocumentsPanel'

type Panel = 'edit' | 'estimate' | 'owner' | 'assign' | 'parts' | 'complete' | 'reject' | 'cancel' | 'feedback' | null

export function MaintenanceDetailPage() {
  const { id = '' } = useParams()
  return <MaintenanceDetailView key={id} id={id} />
}

function MaintenanceDetailView({ id }: { id: string }) {
  const queryClient = useQueryClient()
  const [panel, setPanel] = useState<Panel>(null)
  const request = useQuery({ queryKey: ['maintenance', id], queryFn: () => get<MaintenanceDetail>(`/api/v1/maintenance/${id}`) })
  const done = async (saved?: MaintenanceDetail) => {
    if (saved) queryClient.setQueryData(['maintenance', id], saved)
    await queryClient.invalidateQueries({ queryKey: ['maintenance'] })
    setPanel(null)
  }
  const step = useMutation({
    mutationFn: (action: 'review' | 'approve' | 'start') => post<MaintenanceDetail>(`/api/v1/maintenance/${id}/${action}`),
    onSuccess: done,
  })
  const r = request.data

  const back = (
    <Link to="/app/maintenance" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
      ← Maintenance
    </Link>
  )
  if (!r) {
    return (
      <>
        {back}
        <ErrorBox error={request.error} />
        {request.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }

  const { manage, work } = r.can
  const open = !['Completed', 'Rejected', 'Cancelled'].includes(r.status)
  const early = r.status === 'Submitted' || r.status === 'UnderReview'
  const working = r.status === 'Assigned' || r.status === 'InProgress' || r.status === 'AwaitingParts'
  const awaitingOwner = open && r.needsOwnerApproval && !r.ownerApprovalRecordedAtUtc
  const actions: ReactNode[] = []
  const add = (key: string, node: ReactNode) => actions.push(<span key={key}>{node}</span>)
  if (panel === null) {
    if (manage && r.status === 'Submitted') add('review', <Button variant="secondary" onClick={() => step.mutate('review')}>Start review</Button>)
    if (manage && early) add('approve', <Button onClick={() => (awaitingOwner ? setPanel('owner') : step.mutate('approve'))}>Approve work</Button>)
    if (manage && (r.status === 'Approved' || working)) add('assign', <Button variant={working ? 'secondary' : 'primary'} onClick={() => setPanel('assign')}>{working ? 'Reassign' : 'Assign'}</Button>)
    if (work && (r.status === 'Assigned' || r.status === 'AwaitingParts')) add('start', <Button onClick={() => step.mutate('start')}>{r.status === 'Assigned' ? 'Start work' : 'Resume work'}</Button>)
    if (work && r.status === 'InProgress') add('parts', <Button variant="secondary" onClick={() => setPanel('parts')}>Awaiting parts</Button>)
    if (work && working) add('complete', <Button onClick={() => setPanel('complete')}>Mark completed</Button>)
    if (manage && open) add('estimate', <Button variant="secondary" onClick={() => setPanel('estimate')}>{r.estimateAmount === null ? 'Add estimate' : 'Change estimate'}</Button>)
    if (manage && awaitingOwner && !early) add('owner', <Button variant="secondary" onClick={() => setPanel('owner')}>Record owner's approval</Button>)
    if (manage && open) add('edit', <Button variant="ghost" onClick={() => setPanel('edit')}>Edit</Button>)
    if (manage && early) add('reject', <Button variant="danger" onClick={() => setPanel('reject')}>Reject</Button>)
    if (manage && open) add('cancel', <Button variant="danger" onClick={() => setPanel('cancel')}>Cancel request</Button>)
    if (manage && r.status === 'Completed' && r.tenantRating === null) add('feedback', <Button variant="secondary" onClick={() => setPanel('feedback')}>Record tenant feedback</Button>)
  }

  return (
    <>
      {back}
      <PageHeader title={r.title} description={`${r.reference} · ${r.propertyName}${r.unitCode ? `, unit ${r.unitCode}` : ''}`} actions={actions.length > 0 && <div className="flex flex-wrap gap-2">{actions}</div>} />
      <div className="space-y-6">
        <ErrorBox error={step.error} />
        {panel === 'edit' && <MaintenanceForm request={r} onDone={(saved) => done(saved)} />}
        {panel === 'estimate' && <EstimateForm request={r} onDone={done} />}
        {panel === 'owner' && <OwnerApprovalForm request={r} approveAfter={early} onDone={done} />}
        {panel === 'assign' && <AssignForm request={r} onDone={done} />}
        {panel === 'parts' && <NoteForm request={r} onDone={done} />}
        {panel === 'complete' && <CompleteForm request={r} onDone={done} />}
        {(panel === 'reject' || panel === 'cancel') && <ReasonForm request={r} action={panel} onDone={done} />}
        {panel === 'feedback' && <FeedbackForm request={r} onDone={done} />}
        {awaitingOwner && (
          <p className="rounded-md bg-amber-50 p-3 text-sm text-amber-900 ring-1 ring-amber-200">
            The estimate of {formatNaira(r.estimateAmount)} is above the owner's repair approval limit of {formatNaira(r.approvalLimit)}. Get the owner's
            approval and record it before the work goes ahead.
          </p>
        )}
        <Card>
          <div className="mb-4 flex flex-wrap items-center gap-2">
            <StatusBadge status={r.status} />
            <UrgencyBadge urgency={r.urgency} />
          </div>
          <DetailList
            items={[
              { label: 'Property', value: <Link className="text-brand-700 hover:underline" to={`/app/properties/${r.propertyId}`}>{r.propertyName}</Link> },
              { label: 'Unit', value: r.unitCode ?? 'Common area or whole property' },
              { label: 'Category', value: enumLabel(r.category) },
              { label: 'Tenant', value: r.tenantId ? <Link className="text-brand-700 hover:underline" to={`/app/tenants/${r.tenantId}`}>{r.tenantName}</Link> : null },
              { label: 'Reported by', value: r.reportedBy },
              { label: 'Logged', value: `${formatDate(r.createdAtUtc)}${r.createdByName ? ` by ${r.createdByName}` : ''}` },
              { label: 'Assigned to', value: r.assignedToName },
              { label: 'Vendor', value: r.vendorName ? `${r.vendorName}${r.vendorPhone ? ` · ${r.vendorPhone}` : ''}` : null },
              { label: 'Estimate', value: r.estimateAmount === null ? null : `${formatNaira(r.estimateAmount)}${r.estimateNotes ? ` · ${r.estimateNotes}` : ''}` },
              { label: "Owner's approval", value: r.ownerApprovalNote },
              { label: 'Approved', value: r.approvedAtUtc ? `${formatDate(r.approvedAtUtc)}${r.approvedByName ? ` by ${r.approvedByName}` : ''}` : null },
              { label: 'Completed', value: r.completedOn ? formatDay(r.completedOn) : null },
              { label: 'Final cost', value: r.actualCost === null ? null : formatNaira(r.actualCost) },
              { label: 'Completion notes', value: r.completionNotes },
              { label: r.status === 'Rejected' ? 'Reason rejected' : 'Reason cancelled', value: r.closedReason },
              { label: 'Tenant feedback', value: r.tenantRating === null ? null : `${r.tenantRating} out of 5${r.tenantFeedback ? ` · ${r.tenantFeedback}` : ''}` },
              ...(r.description ? [{ label: 'Details', value: r.description }] : []),
            ]}
          />
          {r.status === 'Completed' && r.actualCost !== null && (
            <p className="mt-4 text-xs text-slate-500">The cost is recorded here. Charging it to the owner as an expense arrives with the finance work.</p>
          )}
        </Card>
        <DocumentsPanel subject="Maintenance" subjectId={r.id} title="Photos and attachments" defaultCategory="Photo" />
        <Timeline
          request={r}
          canComment={manage || work}
          // A posted update refreshes the request but leaves any open form alone.
          onSaved={(saved) => queryClient.setQueryData(['maintenance', id], saved)}
        />
      </div>
    </>
  )
}

function Timeline({ request, canComment, onSaved }: { request: MaintenanceDetail; canComment: boolean; onSaved: (r: MaintenanceDetail) => void }) {
  const [text, setText] = useState('')
  const comment = useMutation({
    mutationFn: () => post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/comments`, { text: text.trim() }),
    onSuccess: (saved) => {
      setText('')
      onSaved(saved)
    },
  })
  return (
    <Card title="Timeline">
      <ol className="space-y-3">
        {request.updates.map((u) => (
          <li key={u.id} className="text-sm">
            <p className="text-slate-800">{describe(u)}</p>
            {u.text && u.kind !== 'Assigned' && u.kind !== 'Estimated' && <p className="whitespace-pre-line text-slate-600">{u.text}</p>}
            <p className="text-xs text-slate-500">
              {formatDate(u.atUtc)}
              {u.byName && ` · ${u.byName}`}
            </p>
          </li>
        ))}
      </ol>
      {canComment && (
        <form
          className="mt-4 space-y-2"
          onSubmit={(e) => {
            e.preventDefault()
            if (text.trim()) comment.mutate()
          }}
        >
          <ErrorBox error={comment.error} />
          <TextAreaField label="Add an update" rows={2} maxLength={2000} value={text} onChange={(e) => setText(e.target.value)} />
          <Button type="submit" variant="secondary" disabled={comment.isPending || !text.trim()}>
            Post update
          </Button>
        </form>
      )}
    </Card>
  )
}

function describe(u: MaintenanceUpdate): string {
  switch (u.kind) {
    case 'Created':
      return 'Request logged'
    case 'Comment':
      return 'Update'
    case 'Assigned':
      return u.text ?? 'Assigned'
    case 'Estimated':
      return u.text ?? 'Estimate added'
    case 'OwnerApproved':
      return "Owner's approval recorded"
    case 'Feedback':
      return 'Tenant feedback recorded'
    default:
      return `Moved to ${enumLabel(u.status).toLowerCase()}`
  }
}

function Panel({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200">
      <h3 className="text-sm font-semibold">{title}</h3>
      {children}
    </section>
  )
}

function Actions({ pending, label, onCancel }: { pending: boolean; label: string; onCancel: () => void }) {
  return (
    <div className="flex gap-2">
      <Button type="submit" disabled={pending}>
        {label}
      </Button>
      <Button type="button" variant="secondary" onClick={onCancel}>
        Cancel
      </Button>
    </div>
  )
}

type FormProps = { request: MaintenanceDetail; onDone: (saved?: MaintenanceDetail) => void }

const estimateSchema = z.object({ amount: requiredAmount, notes: optionalText(1000), ownerApprovalNote: optionalText(500) })

function EstimateForm({ request, onDone }: FormProps) {
  const limit = useQuery({
    queryKey: ['maintenance', 'limit', request.propertyId],
    queryFn: () => get<{ limit: number | null }>(`/api/v1/maintenance/approval-limit?propertyId=${request.propertyId}`),
  })
  const { register, handleSubmit, formState, control } = useForm<z.infer<typeof estimateSchema>>({
    resolver: zodResolver(estimateSchema),
    defaultValues: { amount: request.estimateAmount?.toString() ?? '', notes: request.estimateNotes ?? '', ownerApprovalNote: '' },
  })
  const amount = Number(useWatch({ control, name: 'amount' })) || 0
  const cap = limit.data?.limit ?? null
  const over = cap !== null && amount > cap
  const save = useMutation({
    mutationFn: (v: z.infer<typeof estimateSchema>) =>
      post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/estimate`, {
        amount: Number(v.amount),
        notes: blankToNull(v.notes),
        ownerApprovalNote: over ? blankToNull(v.ownerApprovalNote) : null,
      }),
    onSuccess: onDone,
  })
  return (
    <Panel title="Estimate">
      <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
        <ErrorBox error={save.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Estimated cost (₦)"
            inputMode="decimal"
            hint={cap === null ? "No owner approval limit is set for this property." : `The owner approves anything above ${formatNaira(cap)}.`}
            {...register('amount')}
            error={formState.errors.amount?.message}
          />
          <TextField label="What it covers" placeholder="Parts and labour" {...register('notes')} error={formState.errors.notes?.message} />
        </div>
        {over && (
          <TextField
            label="Owner's approval (if already given)"
            placeholder="Mrs Adebayo approved by WhatsApp on 20 Oct"
            hint="Leave blank to record it later. The work can't be approved until it is recorded."
            {...register('ownerApprovalNote')}
            error={formState.errors.ownerApprovalNote?.message}
          />
        )}
        <Actions pending={save.isPending} label="Save estimate" onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

function OwnerApprovalForm({ request, approveAfter, onDone }: FormProps & { approveAfter: boolean }) {
  const [note, setNote] = useState('')
  const save = useMutation({
    mutationFn: async () => {
      const saved = await post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/owner-approval`, { note: note.trim() })
      return approveAfter ? post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/approve`) : saved
    },
    onSuccess: onDone,
  })
  return (
    <Panel title="Owner's approval">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <ErrorBox error={save.error} />
        <p className="text-sm text-slate-700">
          The estimate of {formatNaira(request.estimateAmount)} is above the owner's limit of {formatNaira(request.approvalLimit)}. Record who approved it and how.
        </p>
        <TextField label="Approval" placeholder="Mrs Adebayo approved by phone" required maxLength={500} value={note} onChange={(e) => setNote(e.target.value)} />
        <Actions pending={save.isPending || !note.trim()} label={approveAfter ? 'Record and approve work' : 'Record approval'} onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

function AssignForm({ request, onDone }: FormProps) {
  const [assignee, setAssignee] = useState(request.assignedUserId ?? '')
  const [vendor, setVendor] = useState(request.vendorId ?? '')
  const assignees = useQuery({ queryKey: ['maintenance', 'assignees'], queryFn: () => get<Assignee[]>('/api/v1/maintenance/assignees') })
  const vendors = useQuery({ queryKey: ['vendors', 'picker'], queryFn: () => get<Paged<Vendor>>('/api/v1/vendors?pageSize=100') })
  const save = useMutation({
    mutationFn: () => post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/assign`, { assignedUserId: assignee || null, vendorId: vendor || null }),
    onSuccess: onDone,
  })
  return (
    <Panel title="Assign the job">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <ErrorBox error={save.error ?? assignees.error ?? vendors.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField label="Staff member" value={assignee} onChange={(e) => setAssignee(e.target.value)} hint="They get an email and can update the job.">
            <option value="">No staff member</option>
            {assignees.data?.map((a) => (
              <option key={a.userId} value={a.userId}>
                {a.fullName}
              </option>
            ))}
          </SelectField>
          <SelectField label="Vendor" value={vendor} onChange={(e) => setVendor(e.target.value)}>
            <option value="">No vendor</option>
            {vendors.data?.items.map((v) => (
              <option key={v.id} value={v.id}>
                {v.name}
                {v.trade ? ` · ${v.trade}` : ''}
              </option>
            ))}
          </SelectField>
        </div>
        {vendors.data?.items.length === 0 && (
          <p className="text-sm text-slate-600">
            No vendors yet. <Link className="font-medium text-brand-700 hover:underline" to="/app/vendors">Add one</Link>.
          </p>
        )}
        <Actions pending={save.isPending || (!assignee && !vendor)} label="Assign" onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

function NoteForm({ request, onDone }: FormProps) {
  const [note, setNote] = useState('')
  const save = useMutation({
    mutationFn: () => post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/awaiting-parts`, { note: blankToNull(note) }),
    onSuccess: onDone,
  })
  return (
    <Panel title="Awaiting parts">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <ErrorBox error={save.error} />
        <TextField label="What's needed" placeholder="1.5 inch sink trap" maxLength={1000} value={note} onChange={(e) => setNote(e.target.value)} />
        <Actions pending={save.isPending} label="Save" onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

const completeSchema = z
  .object({ completedOn: z.string().min(1, 'Enter the date.'), actualCost: optionalAmount, notes: optionalText(2000) })
  .refine((v) => v.completedOn <= todayInLagos(), { path: ['completedOn'], message: "The date can't be in the future." })

function CompleteForm({ request, onDone }: FormProps) {
  const { register, handleSubmit, formState } = useForm<z.infer<typeof completeSchema>>({
    resolver: zodResolver(completeSchema),
    defaultValues: { completedOn: todayInLagos(), actualCost: request.estimateAmount?.toString() ?? '', notes: '' },
  })
  const save = useMutation({
    mutationFn: (v: z.infer<typeof completeSchema>) =>
      post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/complete`, {
        completedOn: v.completedOn,
        actualCost: numberOrNull(v.actualCost),
        notes: blankToNull(v.notes),
      }),
    onSuccess: onDone,
  })
  return (
    <Panel title="Mark completed">
      <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
        <ErrorBox error={save.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Completed on" type="date" {...register('completedOn')} error={formState.errors.completedOn?.message} />
          <TextField label="Final cost (₦)" inputMode="decimal" {...register('actualCost')} error={formState.errors.actualCost?.message} />
        </div>
        <TextAreaField label="What was done" rows={2} {...register('notes')} error={formState.errors.notes?.message} />
        <Actions pending={save.isPending} label="Mark completed" onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

function ReasonForm({ request, action, onDone }: FormProps & { action: 'reject' | 'cancel' }) {
  const [reason, setReason] = useState('')
  const save = useMutation({
    mutationFn: () => post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/${action}`, { reason: reason.trim() }),
    onSuccess: onDone,
  })
  return (
    <Panel title={action === 'reject' ? 'Reject request' : 'Cancel request'}>
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <ErrorBox error={save.error} />
        <TextField label="Reason" required maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        <Actions pending={save.isPending || !reason.trim()} label={action === 'reject' ? 'Reject' : 'Cancel request'} onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}

function FeedbackForm({ request, onDone }: FormProps) {
  const [rating, setRating] = useState('5')
  const [comment, setComment] = useState('')
  const save = useMutation({
    mutationFn: () => post<MaintenanceDetail>(`/api/v1/maintenance/${request.id}/feedback`, { rating: Number(rating), comment: blankToNull(comment) }),
    onSuccess: onDone,
  })
  return (
    <Panel title="Tenant feedback">
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          save.mutate()
        }}
      >
        <ErrorBox error={save.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <SelectField label="Rating" value={rating} onChange={(e) => setRating(e.target.value)}>
            {[5, 4, 3, 2, 1].map((n) => (
              <option key={n} value={n}>
                {n} out of 5
              </option>
            ))}
          </SelectField>
          <TextField label="Comment" maxLength={1000} value={comment} onChange={(e) => setComment(e.target.value)} />
        </div>
        <Actions pending={save.isPending} label="Save feedback" onCancel={() => onDone()} />
      </form>
    </Panel>
  )
}
