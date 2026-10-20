import { useState } from 'react'
import { useFieldArray, useForm, useWatch, type Control } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, put } from '../../lib/api'
import type { InspectionType, LeaseInspection, LeaseInspections, LeaseStatus } from '../../lib/types'
import { ItemConditions } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { formatDay, formatNaira, todayInLagos } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, numberOrNull, optionalAmount, optionalText } from '../../lib/validation'
import { Badge, Button, Card, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

const defaultAreas = ['Living room', 'Kitchen', 'Bedroom 1', 'Bathroom', 'Doors and windows', 'Electrical fittings', 'Plumbing']

/** Move-in and move-out condition reports. Move-out areas can propose deductions from the caution deposit. */
export function InspectionsPanel({ leaseId, status }: { leaseId: string; status: LeaseStatus }) {
  const { can } = useSession()
  const [editing, setEditing] = useState<InspectionType | null>(null)
  const reports = useQuery({
    queryKey: ['leases', leaseId, 'inspections'],
    queryFn: () => get<LeaseInspections>(`/api/v1/leases/${leaseId}/inspections`),
  })
  const canEdit = can(Permissions.leasesCreate)
  const canMoveIn = canEdit && (status === 'Draft' || status === 'Active')
  const canMoveOut = canEdit && (status === 'Active' || status === 'Ended' || status === 'Terminated')
  const data = reports.data

  return (
    <Card title="Inspections">
      <ErrorBox error={reports.error} />
      {editing && (
        <InspectionForm
          leaseId={leaseId}
          type={editing}
          existing={editing === 'MoveIn' ? (data?.moveIn ?? null) : (data?.moveOut ?? null)}
          moveIn={data?.moveIn ?? null}
          onDone={() => setEditing(null)}
        />
      )}
      {data && !editing && (
        <div className="grid gap-6 sm:grid-cols-2">
          <Report
            title="Move-in"
            report={data.moveIn}
            action={canMoveIn ? () => setEditing('MoveIn') : undefined}
          />
          <Report
            title="Move-out"
            report={data.moveOut}
            deposit={data.cautionDeposit}
            action={canMoveOut ? () => setEditing('MoveOut') : undefined}
          />
        </div>
      )}
    </Card>
  )
}

function Report({ title, report, deposit, action }: { title: string; report: LeaseInspection | null; deposit?: number | null; action?: () => void }) {
  return (
    <section className="min-w-0">
      <div className="mb-2 flex items-center justify-between gap-2">
        <h3 className="text-sm font-semibold">{title}</h3>
        {action && (
          <Button variant="ghost" onClick={action}>
            {report ? `Edit ${title.toLowerCase()} report` : `Record ${title.toLowerCase()} report`}
          </Button>
        )}
      </div>
      {!report && <p className="text-sm text-slate-500">Not recorded.</p>}
      {report && (
        <div className="space-y-2 text-sm">
          <p className="text-slate-600">
            {formatDay(report.inspectedOn)}
            {report.inspectedByName && ` · ${report.inspectedByName}`} ·{' '}
            {report.tenantPresent ? (report.tenantAgreed ? 'tenant present and agreed' : 'tenant present, not agreed') : 'tenant not present'}
          </p>
          {report.summary && <p>{report.summary}</p>}
          <ul className="divide-y divide-slate-100 rounded-md ring-1 ring-slate-200">
            {report.items.map((i, n) => (
              <li key={n} className="flex items-start justify-between gap-2 px-3 py-2">
                <span className="min-w-0">
                  {i.area}
                  {i.notes && <span className="block text-xs text-slate-500">{i.notes}</span>}
                </span>
                <span className="flex shrink-0 flex-col items-end gap-1">
                  <Badge tone={statusTone(i.condition)}>{i.condition}</Badge>
                  {i.proposedDeduction !== null && <span className="text-xs tabular-nums">{formatNaira(i.proposedDeduction)}</span>}
                </span>
              </li>
            ))}
          </ul>
          {report.type === 'MoveOut' && report.totalProposedDeduction > 0 && (
            <p>
              Proposed deductions: <span className="font-semibold tabular-nums">{formatNaira(report.totalProposedDeduction)}</span>
              {deposit !== null && deposit !== undefined && (
                <span className="text-slate-600"> of a {formatNaira(deposit)} deposit</span>
              )}
              {deposit !== null && deposit !== undefined && report.totalProposedDeduction > deposit && (
                <span className="block text-xs text-red-700">More than the deposit held.</span>
              )}
              <span className="block text-xs text-slate-500">A proposal only. Deposit refunds arrive with the finance work.</span>
            </p>
          )}
        </div>
      )}
    </section>
  )
}

const itemSchema = z.object({
  area: z.string().trim().min(1, 'Name the area.').max(100),
  condition: z.enum(ItemConditions),
  notes: optionalText(1000),
  proposedDeduction: optionalAmount,
})
const schema = z
  .object({
    inspectedOn: z.string().min(1, 'Enter the date.'),
    tenantPresent: z.boolean(),
    tenantAgreed: z.boolean(),
    summary: optionalText(2000),
    items: z.array(itemSchema).min(1, 'Add at least one area.').max(100),
  })
  .refine((v) => v.inspectedOn <= todayInLagos(), { path: ['inspectedOn'], message: "The date can't be in the future." })
  .refine((v) => v.tenantPresent || !v.tenantAgreed, { path: ['tenantAgreed'], message: 'The tenant must be present to agree.' })
type Values = z.infer<typeof schema>

function InspectionForm({
  leaseId,
  type,
  existing,
  moveIn,
  onDone,
}: {
  leaseId: string
  type: InspectionType
  existing: LeaseInspection | null
  moveIn: LeaseInspection | null
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const moveOut = type === 'MoveOut'
  // A move-out report starts from the move-in areas, so the two can be compared line by line.
  const startingAreas = existing?.items ?? moveIn?.items.map((i) => ({ ...i, notes: null, proposedDeduction: null })) ?? defaultAreas.map((area) => ({ area, condition: 'Good' as const, notes: null, proposedDeduction: null }))
  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      inspectedOn: existing?.inspectedOn ?? todayInLagos(),
      tenantPresent: existing?.tenantPresent ?? true,
      tenantAgreed: existing?.tenantAgreed ?? false,
      summary: existing?.summary ?? '',
      items: startingAreas.map((i) => ({
        area: i.area,
        condition: i.condition,
        notes: i.notes ?? '',
        proposedDeduction: i.proposedDeduction?.toString() ?? '',
      })),
    },
  })
  const items = useFieldArray({ control, name: 'items' })
  const save = useMutation({
    mutationFn: (v: Values) =>
      put<LeaseInspection>(`/api/v1/leases/${leaseId}/inspections/${type}`, {
        inspectedOn: v.inspectedOn,
        tenantPresent: v.tenantPresent,
        tenantAgreed: v.tenantAgreed,
        summary: blankToNull(v.summary),
        items: v.items.map((i) => ({
          area: i.area.trim(),
          condition: i.condition,
          notes: blankToNull(i.notes),
          proposedDeduction: moveOut ? numberOrNull(i.proposedDeduction) : null,
        })),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['leases', leaseId, 'inspections'] })
      onDone()
    },
  })
  const errors = formState.errors

  return (
    <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
      <h3 className="text-sm font-semibold">{moveOut ? 'Move-out report' : 'Move-in report'}</h3>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-3">
        <TextField label="Inspection date" type="date" {...register('inspectedOn')} error={errors.inspectedOn?.message} />
        <label className="flex items-center gap-2 text-sm sm:mt-6">
          <input type="checkbox" {...register('tenantPresent')} /> Tenant present
        </label>
        <div className="sm:mt-6">
          <label className="flex items-center gap-2 text-sm">
            <input type="checkbox" {...register('tenantAgreed')} /> Tenant agreed the report
          </label>
          {errors.tenantAgreed && <p className="text-xs text-red-600">{errors.tenantAgreed.message}</p>}
        </div>
      </div>
      <div className="space-y-3">
        {items.fields.map((field, i) => (
          <div
            key={field.id}
            className={`grid items-start gap-3 rounded-md bg-slate-50 p-3 ring-1 ring-slate-200 ${moveOut ? 'sm:grid-cols-[1fr_8rem_1fr_9rem_auto]' : 'sm:grid-cols-[1fr_8rem_1fr_auto]'}`}
          >
            <TextField label="Area" id={`items.${i}.area`} {...register(`items.${i}.area`)} error={errors.items?.[i]?.area?.message} />
            <SelectField label="Condition" id={`items.${i}.condition`} {...register(`items.${i}.condition`)}>
              {ItemConditions.map((c) => (
                <option key={c} value={c}>
                  {c}
                </option>
              ))}
            </SelectField>
            <TextField label="Notes" id={`items.${i}.notes`} {...register(`items.${i}.notes`)} error={errors.items?.[i]?.notes?.message} />
            {moveOut && (
              <TextField
                label="Deduct (₦)"
                id={`items.${i}.proposedDeduction`}
                inputMode="decimal"
                {...register(`items.${i}.proposedDeduction`)}
                error={errors.items?.[i]?.proposedDeduction?.message}
              />
            )}
            <Button type="button" variant="ghost" className="sm:mt-6" aria-label={`Remove area ${i + 1}`} onClick={() => items.remove(i)}>
              Remove
            </Button>
          </div>
        ))}
        {errors.items?.message && <p className="text-xs text-red-600">{errors.items.message}</p>}
        <Button type="button" variant="secondary" onClick={() => items.append({ area: '', condition: 'Good', notes: '', proposedDeduction: '' })}>
          Add area
        </Button>
        {moveOut && <DeductionTotal control={control} />}
      </div>
      <TextAreaField label="Summary" rows={2} {...register('summary')} error={errors.summary?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save report
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}

function DeductionTotal({ control }: { control: Control<Values> }) {
  const items = useWatch({ control, name: 'items' })
  const total = items.reduce((sum, i) => sum + (Number(i.proposedDeduction) || 0), 0)
  if (total <= 0) return null
  return <p className="text-sm">Proposed deductions: <span className="font-semibold tabular-nums">{formatNaira(Math.round(total * 100) / 100)}</span></p>
}
