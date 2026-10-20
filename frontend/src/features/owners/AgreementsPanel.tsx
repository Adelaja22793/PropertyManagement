import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Agreement, OwnedProperty } from '../../lib/types'
import { AdvanceRentReleases, FeeBases, PayoutFrequencies } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDay, formatNaira, todayInLagos } from '../../lib/format'
import { blankToNull, numberOrNull, optionalAmount, optionalText, requiredAmount } from '../../lib/validation'
import { Button, Card, Empty, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

function feeText(a: Agreement) {
  return a.feeBasis === 'FixedAmountPerMonth' ? `${formatNaira(a.feeValue)} per month` : `${a.feeValue}% ${enumLabel(a.feeBasis).replace('% ', '')}`
}

export function AgreementsPanel({ ownerId, properties }: { ownerId: string; properties: OwnedProperty[] }) {
  const { can } = useSession()
  const canManage = can(Permissions.ownersManage)
  const [editing, setEditing] = useState<Agreement | 'new' | null>(null)
  const agreements = useQuery({
    queryKey: ['owners', ownerId, 'agreements'],
    queryFn: () => get<Agreement[]>(`/api/v1/owners/${ownerId}/agreements`),
  })

  return (
    <Card
      title="Management agreements"
      actions={
        canManage &&
        editing === null && (
          <Button variant="secondary" onClick={() => setEditing('new')}>
            Add agreement
          </Button>
        )
      }
    >
      {editing !== null && (
        <AgreementForm
          ownerId={ownerId}
          properties={properties}
          agreement={editing === 'new' ? null : editing}
          onDone={() => setEditing(null)}
        />
      )}
      <ErrorBox error={agreements.error} />
      {agreements.data?.length === 0 && <Empty>No agreements yet. Record the fee and payout terms agreed with this owner.</Empty>}
      <ul className="divide-y divide-slate-100">
        {agreements.data?.map((a) => (
          <li key={a.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
            <div className="min-w-0 text-sm">
              <p className="font-medium">{a.propertyName ?? 'All properties'}</p>
              <p className="text-slate-700">
                {feeText(a)}
                {a.lettingFeePercent !== null && ` · letting fee ${a.lettingFeePercent}%`}
              </p>
              <p className="text-xs text-slate-500">
                Payouts {enumLabel(a.payoutFrequency).toLowerCase()} · advance rent released {enumLabel(a.advanceRentRelease).toLowerCase()}
                {a.reserveAmount > 0 && ` · reserve ${formatNaira(a.reserveAmount)}`}
                {a.repairApprovalLimit !== null && ` · repairs over ${formatNaira(a.repairApprovalLimit)} need approval`}
              </p>
              <p className="text-xs text-slate-500">
                {formatDay(a.startDate)} – {a.endDate ? formatDay(a.endDate) : 'open-ended'}
              </p>
            </div>
            {canManage && editing === null && (
              <Button variant="ghost" onClick={() => setEditing(a)}>
                Edit
              </Button>
            )}
          </li>
        ))}
      </ul>
    </Card>
  )
}

const schema = z
  .object({
    propertyId: z.string(),
    feeBasis: z.enum(FeeBases),
    feeValue: requiredAmount,
    lettingFeePercent: optionalAmount.refine((v) => v === '' || Number(v) <= 100, 'Enter a percentage up to 100.'),
    payoutFrequency: z.enum(PayoutFrequencies),
    advanceRentRelease: z.enum(AdvanceRentReleases),
    reserveAmount: optionalAmount,
    repairApprovalLimit: optionalAmount,
    startDate: z.string().min(1, 'Choose a start date.'),
    endDate: z.string(),
    notes: optionalText(2000),
  })
  .refine((v) => v.feeBasis === 'FixedAmountPerMonth' || Number(v.feeValue) <= 100, {
    message: 'A percentage fee can’t be more than 100%.',
    path: ['feeValue'],
  })
  .refine((v) => !v.endDate || v.endDate >= v.startDate, { message: 'End date can’t be before start date.', path: ['endDate'] })
type Values = z.infer<typeof schema>

function AgreementForm({
  ownerId,
  properties,
  agreement,
  onDone,
}: {
  ownerId: string
  properties: OwnedProperty[]
  agreement: Agreement | null
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      propertyId: agreement?.propertyId ?? '',
      feeBasis: agreement?.feeBasis ?? 'PercentOfRentCollected',
      feeValue: agreement?.feeValue.toString() ?? '',
      lettingFeePercent: agreement?.lettingFeePercent?.toString() ?? '',
      payoutFrequency: agreement?.payoutFrequency ?? 'Monthly',
      advanceRentRelease: agreement?.advanceRentRelease ?? 'OnReceipt',
      reserveAmount: agreement?.reserveAmount.toString() ?? '0',
      repairApprovalLimit: agreement?.repairApprovalLimit?.toString() ?? '',
      startDate: agreement?.startDate ?? todayInLagos(),
      endDate: agreement?.endDate ?? '',
      notes: agreement?.notes ?? '',
    },
  })
  const errors = formState.errors
  const fixed = useWatch({ control, name: 'feeBasis' }) === 'FixedAmountPerMonth'
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        propertyId: v.propertyId || null,
        feeBasis: v.feeBasis,
        feeValue: Number(v.feeValue),
        lettingFeePercent: numberOrNull(v.lettingFeePercent),
        payoutFrequency: v.payoutFrequency,
        advanceRentRelease: v.advanceRentRelease,
        reserveAmount: numberOrNull(v.reserveAmount) ?? 0,
        repairApprovalLimit: numberOrNull(v.repairApprovalLimit),
        startDate: v.startDate,
        endDate: v.endDate || null,
        notes: blankToNull(v.notes),
      }
      return agreement
        ? put<Agreement>(`/api/v1/owners/${ownerId}/agreements/${agreement.id}`, body)
        : post<Agreement>(`/api/v1/owners/${ownerId}/agreements`, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['owners', ownerId, 'agreements'] })
      onDone()
    },
  })

  // An agreement for a property this owner no longer holds still shows its property.
  const options = [...properties]
  if (agreement?.propertyId && !options.some((p) => p.propertyId === agreement.propertyId)) {
    options.push({ propertyId: agreement.propertyId, code: '', name: agreement.propertyName ?? 'Property', sharePercent: 0, effectiveFrom: '' })
  }

  return (
    <form className="mb-4 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
      <h3 className="text-sm font-semibold">{agreement ? 'Edit agreement' : 'New agreement'}</h3>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField label="Applies to" disabled={!!agreement} {...register('propertyId')}>
          <option value="">All properties</option>
          {options.map((p) => (
            <option key={p.propertyId} value={p.propertyId}>
              {p.code ? `${p.code} · ${p.name}` : p.name}
            </option>
          ))}
        </SelectField>
        <SelectField label="Fee basis" {...register('feeBasis')}>
          {FeeBases.map((b) => (
            <option key={b} value={b}>
              {enumLabel(b)}
            </option>
          ))}
        </SelectField>
        <TextField label={fixed ? 'Fee per month (₦)' : 'Fee (%)'} inputMode="decimal" {...register('feeValue')} error={errors.feeValue?.message} />
        <TextField label="Letting fee (%)" inputMode="decimal" hint="Optional, charged on a new letting." {...register('lettingFeePercent')} error={errors.lettingFeePercent?.message} />
        <SelectField label="Payout frequency" {...register('payoutFrequency')}>
          {PayoutFrequencies.map((f) => (
            <option key={f} value={f}>
              {enumLabel(f)}
            </option>
          ))}
        </SelectField>
        <SelectField label="Advance rent release" {...register('advanceRentRelease')}>
          {AdvanceRentReleases.map((r) => (
            <option key={r} value={r}>
              {r === 'OnReceipt' ? 'On receipt (pay out when received)' : 'By period (as each period starts)'}
            </option>
          ))}
        </SelectField>
        <TextField label="Reserve (₦)" inputMode="decimal" hint="Held back from each payout." {...register('reserveAmount')} error={errors.reserveAmount?.message} />
        <TextField
          label="Repair approval limit (₦)"
          inputMode="decimal"
          hint="Repairs estimated above this need the owner's approval. Blank for no limit."
          {...register('repairApprovalLimit')}
          error={errors.repairApprovalLimit?.message}
        />
        <TextField label="Start date" type="date" {...register('startDate')} error={errors.startDate?.message} />
        <TextField label="End date" type="date" hint="Leave blank if open-ended." {...register('endDate')} error={errors.endDate?.message} />
      </div>
      <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save agreement
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
