import { useState } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useFieldArray, useForm, useWatch, type Control } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueries, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Guarantor, LeaseDetail, Paged, PropertySummary, TenantSummary, Unit } from '../../lib/types'
import { BillingFrequencies, ChargeRecurrences, LeaseChargeTypes } from '../../lib/types'
import { enumLabel, formatNaira, todayInLagos } from '../../lib/format'
import { useDebounced } from '../../lib/hooks'
import { blankToNull, numberOrNull, optionalAmount, optionalText, optionalWhole, requiredAmount } from '../../lib/validation'
import { Button, Card, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { defaultEndDate, moveInTotal } from './leaseMath'

const oneOffOnly = ['AgencyFee', 'LegalFee', 'CautionDeposit', 'StampDuty']

const chargeSchema = z
  .object({
    type: z.enum(LeaseChargeTypes),
    description: optionalText(200),
    amount: requiredAmount.refine((v) => Number(v) > 0, 'Enter an amount above zero.'),
    recurrence: z.enum(ChargeRecurrences),
  })
  .refine((c) => c.type !== 'Other' || c.description !== '', { path: ['description'], message: 'Describe the charge.' })
  .refine((c) => !oneOffOnly.includes(c.type) || c.recurrence === 'OneOff', {
    path: ['recurrence'],
    message: 'This charge is paid once, at move-in.',
  })

const schema = z
  .object({
    propertyId: z.string().min(1, 'Choose a property.'),
    unitId: z.string().min(1, 'Choose a unit.'),
    startDate: z.string().min(1, 'Enter the start date.'),
    endDate: z.string().min(1, 'Enter the end date.'),
    rentAmount: requiredAmount.refine((v) => Number(v) > 0, 'Enter the rent.'),
    billingFrequency: z.enum(BillingFrequencies),
    dueDaysBeforePeriod: optionalWhole,
    graceDays: optionalWhole,
    noticePeriodDays: optionalWhole,
    holdoverRentAmount: optionalAmount,
    renewalTerms: optionalText(1000),
    notes: optionalText(2000),
    charges: z.array(chargeSchema).max(30),
  })
  .refine((v) => !v.startDate || !v.endDate || v.endDate > v.startDate, {
    path: ['endDate'],
    message: 'The end date must be after the start date.',
  })
type Values = z.infer<typeof schema>

type PickedTenant = { id: string; name: string }

/** Create a draft lease, or edit one. Nothing is billed until Phase 4; activating it marks the unit as let. */
export function LeaseEditorPage() {
  const { id } = useParams()
  const [params] = useSearchParams()
  const presetTenant = id ? null : params.get('tenantId')
  const existing = useQuery({
    queryKey: ['leases', id],
    queryFn: () => get<LeaseDetail>(`/api/v1/leases/${id}`),
    enabled: !!id,
  })
  // Starting from a tenant's page names that tenant on the new lease.
  const tenant = useQuery({
    queryKey: ['tenants', presetTenant],
    queryFn: () => get<PickedTenant>(`/api/v1/tenants/${presetTenant}`),
    enabled: !!presetTenant,
  })
  // Starting from a unit fills in its asking rent.
  const presetUnit = id ? null : params.get('unitId')
  const unit = useQuery({
    queryKey: ['units', presetUnit],
    queryFn: () => get<Unit>(`/api/v1/units/${presetUnit}`),
    enabled: !!presetUnit,
  })
  const pending = (id && !existing.data) || (presetTenant && tenant.isLoading) || (presetUnit && unit.isLoading)
  if (pending) {
    return (
      <>
        <ErrorBox error={existing.error} />
        {(existing.isLoading || tenant.isLoading || unit.isLoading) && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }
  const lease = existing.data ?? null
  const initialTenants = lease
    ? lease.tenants.map((t) => ({ id: t.tenantId, name: t.name }))
    : tenant.data
      ? [{ id: tenant.data.id, name: tenant.data.name }]
      : []
  return <LeaseEditor lease={lease} initialTenants={initialTenants} unit={unit.data ?? null} />
}

function LeaseEditor({ lease, initialTenants, unit }: { lease: LeaseDetail | null; initialTenants: PickedTenant[]; unit: Unit | null }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const [tenants, setTenants] = useState<PickedTenant[]>(initialTenants)
  const [guarantorIds, setGuarantorIds] = useState<string[]>(lease?.guarantors.map((g) => g.guarantorId) ?? [])
  const [tenantError, setTenantError] = useState<string | null>(null)

  const start = todayInLagos()
  const { register, handleSubmit, formState, control, setValue, getValues } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      propertyId: lease?.propertyId ?? params.get('propertyId') ?? '',
      unitId: lease?.unitId ?? params.get('unitId') ?? '',
      startDate: lease?.startDate ?? start,
      endDate: lease?.endDate ?? defaultEndDate(start),
      rentAmount: lease?.rentAmount.toString() ?? unit?.defaultRent.toString() ?? '',
      billingFrequency: lease?.billingFrequency ?? unit?.billingFrequency ?? 'Annual',
      dueDaysBeforePeriod: lease?.dueDaysBeforePeriod.toString() ?? '0',
      graceDays: lease?.graceDays.toString() ?? '7',
      noticePeriodDays: lease?.noticePeriodDays?.toString() ?? '',
      holdoverRentAmount: lease?.holdoverRentAmount?.toString() ?? '',
      renewalTerms: lease?.renewalTerms ?? '',
      notes: lease?.notes ?? '',
      charges:
        lease?.charges.map((c) => ({ type: c.type, description: c.description ?? '', amount: c.amount.toString(), recurrence: c.recurrence })) ?? [],
    },
  })
  const errors = formState.errors
  const charges = useFieldArray({ control, name: 'charges' })
  const propertyId = useWatch({ control, name: 'propertyId' })

  const properties = useQuery({
    queryKey: ['properties', 'options'],
    queryFn: () => get<Paged<PropertySummary>>('/api/v1/properties?pageSize=100'),
    enabled: !lease,
  })
  const units = useQuery({
    queryKey: ['properties', propertyId, 'units', false],
    queryFn: () => get<Unit[]>(`/api/v1/properties/${propertyId}/units`),
    enabled: !lease && !!propertyId,
  })
  const guarantors = useQueries({
    queries: tenants.map((t) => ({
      queryKey: ['tenants', t.id, 'guarantors'],
      queryFn: () => get<Guarantor[]>(`/api/v1/tenants/${t.id}/guarantors`),
    })),
  })
  const availableGuarantors = guarantors.flatMap((g) => g.data ?? [])

  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        unitId: v.unitId,
        tenantIds: tenants.map((t) => t.id),
        guarantorIds: guarantorIds.filter((g) => availableGuarantors.some((a) => a.id === g)),
        startDate: v.startDate,
        endDate: v.endDate,
        rentAmount: Number(v.rentAmount),
        currency: 'NGN',
        billingFrequency: v.billingFrequency,
        dueDaysBeforePeriod: numberOrNull(v.dueDaysBeforePeriod) ?? 0,
        graceDays: numberOrNull(v.graceDays) ?? 0,
        noticePeriodDays: numberOrNull(v.noticePeriodDays),
        holdoverRentAmount: numberOrNull(v.holdoverRentAmount),
        renewalTerms: blankToNull(v.renewalTerms),
        notes: blankToNull(v.notes),
        charges: v.charges.map((c) => ({
          type: c.type,
          description: blankToNull(c.description),
          amount: Number(c.amount),
          recurrence: c.recurrence,
        })),
      }
      return lease ? put<LeaseDetail>(`/api/v1/leases/${lease.id}`, body) : post<LeaseDetail>('/api/v1/leases', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['leases'] })
      navigate(`/app/leases/${saved.id}`)
    },
  })

  const submit = handleSubmit((v) => {
    if (tenants.length === 0) {
      setTenantError('Add at least one tenant.')
      return
    }
    save.mutate(v)
  })

  return (
    <>
      <Link to={lease ? `/app/leases/${lease.id}` : '/app/leases'} className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
        ← {lease ? lease.reference : 'Leases'}
      </Link>
      <PageHeader
        title={lease ? `Edit ${lease.reference}` : 'New lease'}
        description="Saved as a draft. Activate it when the tenancy agreement is signed."
      />
      <form className="space-y-6" onSubmit={submit} noValidate>
        <ErrorBox error={save.error} />
        <Card title="Unit">
          {lease ? (
            <p className="text-sm text-slate-700">
              {lease.propertyName}, unit {lease.unitCode}
            </p>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2">
              <SelectField
                label="Property"
                {...register('propertyId', { onChange: () => setValue('unitId', '') })}
                error={errors.propertyId?.message}
              >
                <option value="">Choose a property…</option>
                {properties.data?.items.map((p) => (
                  <option key={p.id} value={p.id}>
                    {p.name} ({p.code})
                  </option>
                ))}
              </SelectField>
              <SelectField
                label="Unit"
                {...register('unitId', {
                  onChange: (e) => {
                    const unit = units.data?.find((u) => u.id === e.target.value)
                    if (unit && !getValues('rentAmount')) {
                      setValue('rentAmount', unit.defaultRent.toString())
                      setValue('billingFrequency', unit.billingFrequency)
                    }
                  },
                })}
                error={errors.unitId?.message}
                disabled={!propertyId}
              >
                <option value="">{propertyId ? 'Choose a unit…' : 'Choose a property first'}</option>
                {units.data
                  ?.filter((u) => !u.isArchived)
                  .map((u) => (
                    <option key={u.id} value={u.id}>
                      {u.code} · {enumLabel(u.type)} · {u.occupancyStatus}
                    </option>
                  ))}
              </SelectField>
            </div>
          )}
        </Card>

        <Card title="Tenants and guarantors">
          <TenantPicker
            picked={tenants}
            onChange={(next) => {
              setTenants(next)
              setTenantError(null)
            }}
          />
          {tenantError && <p className="mt-2 text-xs text-red-600">{tenantError}</p>}
          <div className="mt-5">
            <h3 className="text-sm font-medium text-slate-700">Guarantors</h3>
            {tenants.length > 0 && availableGuarantors.length === 0 && (
              <p className="mt-1 text-sm text-slate-500">These tenants have no guarantors yet. Add them on the tenant's page.</p>
            )}
            <div className="mt-2 space-y-2">
              {availableGuarantors.map((g) => (
                <label key={g.id} className="flex items-center gap-2 text-sm">
                  <input
                    type="checkbox"
                    checked={guarantorIds.includes(g.id)}
                    onChange={(e) => setGuarantorIds((ids) => (e.target.checked ? [...ids, g.id] : ids.filter((x) => x !== g.id)))}
                  />
                  {g.fullName}
                  <span className="text-slate-500">
                    for {tenants.find((t) => t.id === g.tenantId)?.name}
                    {!g.signedFormReceived && ' · form not received'}
                  </span>
                </label>
              ))}
            </div>
          </div>
        </Card>

        <Card title="Term and rent">
          <div className="grid gap-4 sm:grid-cols-3">
            <TextField
              label="Start date"
              type="date"
              {...register('startDate', {
                onChange: (e) => {
                  if (e.target.value && !lease) setValue('endDate', defaultEndDate(e.target.value))
                },
              })}
              error={errors.startDate?.message}
            />
            <TextField label="End date (last day)" type="date" {...register('endDate')} error={errors.endDate?.message} />
            <SelectField label="Billing frequency" {...register('billingFrequency')}>
              {BillingFrequencies.map((f) => (
                <option key={f} value={f}>
                  {enumLabel(f)}
                </option>
              ))}
            </SelectField>
            <AmountField control={control} name="rentAmount" label="Rent per period (₦)" register={register} error={errors.rentAmount?.message} />
            <TextField
              label="Due days before each period"
              inputMode="numeric"
              hint="0 means rent is due on the first day."
              {...register('dueDaysBeforePeriod')}
              error={errors.dueDaysBeforePeriod?.message}
            />
            <TextField label="Grace period (days)" inputMode="numeric" {...register('graceDays')} error={errors.graceDays?.message} />
            <TextField
              label="Notice period (days)"
              inputMode="numeric"
              hint="Check your state's tenancy law."
              {...register('noticePeriodDays')}
              error={errors.noticePeriodDays?.message}
            />
            <AmountField
              control={control}
              name="holdoverRentAmount"
              label="Holdover rent per period (₦)"
              register={register}
              error={errors.holdoverRentAmount?.message}
              fallbackHint="If the tenant stays on after the end date."
            />
          </div>
          <div className="mt-4 grid gap-4 sm:grid-cols-2">
            <TextAreaField label="Renewal terms" rows={2} {...register('renewalTerms')} error={errors.renewalTerms?.message} />
            <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
          </div>
        </Card>

        <Card
          title="Other charges"
          actions={
            <Button
              type="button"
              variant="secondary"
              onClick={() => charges.append({ type: 'CautionDeposit', description: '', amount: '', recurrence: 'OneOff' })}
            >
              Add charge
            </Button>
          }
        >
          {charges.fields.length === 0 && (
            <p className="text-sm text-slate-500">Add the caution deposit, agency and legal fees, service charge and any others.</p>
          )}
          <div className="space-y-3">
            {charges.fields.map((field, i) => (
              <div key={field.id} className="grid items-start gap-3 rounded-md bg-slate-50 p-3 ring-1 ring-slate-200 sm:grid-cols-[1fr_1fr_1fr_1fr_auto]">
                <SelectField
                  label="Type"
                  id={`charges.${i}.type`}
                  {...register(`charges.${i}.type`, {
                    onChange: (e) => {
                      if (oneOffOnly.includes(e.target.value)) setValue(`charges.${i}.recurrence`, 'OneOff')
                      if (e.target.value === 'ServiceCharge') setValue(`charges.${i}.recurrence`, 'EachPeriod')
                    },
                  })}
                >
                  {LeaseChargeTypes.map((t) => (
                    <option key={t} value={t}>
                      {enumLabel(t)}
                    </option>
                  ))}
                </SelectField>
                <TextField
                  label="Description"
                  id={`charges.${i}.description`}
                  {...register(`charges.${i}.description`)}
                  error={errors.charges?.[i]?.description?.message}
                />
                <TextField
                  label="Amount (₦)"
                  id={`charges.${i}.amount`}
                  inputMode="decimal"
                  {...register(`charges.${i}.amount`)}
                  error={errors.charges?.[i]?.amount?.message}
                />
                <SelectField
                  label="When"
                  id={`charges.${i}.recurrence`}
                  {...register(`charges.${i}.recurrence`)}
                  error={errors.charges?.[i]?.recurrence?.message}
                >
                  {ChargeRecurrences.map((r) => (
                    <option key={r} value={r}>
                      {enumLabel(r)}
                    </option>
                  ))}
                </SelectField>
                <Button type="button" variant="ghost" className="sm:mt-6" aria-label={`Remove charge ${i + 1}`} onClick={() => charges.remove(i)}>
                  Remove
                </Button>
              </div>
            ))}
          </div>
          <MoveInTotal control={control} />
        </Card>

        <div className="flex gap-2">
          <Button type="submit" disabled={save.isPending}>
            {lease ? 'Save draft' : 'Create draft lease'}
          </Button>
          <Button type="button" variant="secondary" onClick={() => navigate(lease ? `/app/leases/${lease.id}` : '/app/leases')}>
            Cancel
          </Button>
        </div>
      </form>
    </>
  )
}

function AmountField({
  control,
  name,
  label,
  register,
  error,
  fallbackHint,
}: {
  control: Control<Values>
  name: 'rentAmount' | 'holdoverRentAmount'
  label: string
  register: ReturnType<typeof useForm<Values>>['register']
  error?: string
  fallbackHint?: string
}) {
  const value = useWatch({ control, name })
  const valid = /^\d+(\.\d{1,2})?$/.test(value.trim())
  return <TextField label={label} inputMode="decimal" hint={valid ? formatNaira(Number(value)) : fallbackHint} {...register(name)} error={error} />
}

function MoveInTotal({ control }: { control: Control<Values> }) {
  const rent = useWatch({ control, name: 'rentAmount' })
  const charges = useWatch({ control, name: 'charges' })
  const amounts = charges.map((c) => ({ amount: Number(c.amount) || 0, recurrence: c.recurrence }))
  if (!(Number(rent) > 0)) return null
  return (
    <p className="mt-4 text-sm text-slate-700">
      Move-in total: <span className="font-semibold tabular-nums">{formatNaira(moveInTotal(Number(rent), amounts))}</span>
      <span className="block text-xs text-slate-500">First period's rent plus these charges. Invoicing starts in Phase 4.</span>
    </p>
  )
}

/** Search tenants and build the list for the lease. The first one is the primary tenant. */
function TenantPicker({ picked, onChange }: { picked: PickedTenant[]; onChange: (next: PickedTenant[]) => void }) {
  const [search, setSearch] = useState('')
  const term = useDebounced(search.trim())
  const results = useQuery({
    queryKey: ['tenants', 'list', term, 1],
    queryFn: () => get<Paged<TenantSummary>>(`/api/v1/tenants?search=${encodeURIComponent(term)}&pageSize=8`),
    enabled: term.length > 0,
  })
  const options = results.data?.items.filter((t) => !picked.some((p) => p.id === t.id)) ?? []

  return (
    <div>
      {picked.length > 0 && (
        <ul className="mb-3 divide-y divide-slate-100 rounded-md ring-1 ring-slate-200">
          {picked.map((t, i) => (
            <li key={t.id} className="flex items-center justify-between gap-3 px-3 py-2 text-sm">
              <span>
                {t.name} {i === 0 && <span className="text-xs text-slate-500">(primary tenant)</span>}
              </span>
              <span className="flex gap-1">
                {i > 0 && (
                  <Button type="button" variant="ghost" onClick={() => onChange([t, ...picked.filter((p) => p.id !== t.id)])}>
                    Make primary
                  </Button>
                )}
                <Button type="button" variant="ghost" aria-label={`Remove ${t.name}`} onClick={() => onChange(picked.filter((p) => p.id !== t.id))}>
                  Remove
                </Button>
              </span>
            </li>
          ))}
        </ul>
      )}
      <TextField
        label={picked.length ? 'Add another tenant' : 'Find a tenant'}
        name="tenant-search"
        type="search"
        placeholder="Name, email or phone"
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        autoComplete="off"
      />
      {options.length > 0 && (
        <ul className="mt-1 divide-y divide-slate-100 rounded-md bg-white shadow-sm ring-1 ring-slate-200" aria-label="Matching tenants">
          {options.map((t) => (
            <li key={t.id}>
              <button
                type="button"
                className="block w-full px-3 py-2 text-left text-sm hover:bg-slate-50"
                onClick={() => {
                  onChange([...picked, { id: t.id, name: t.name }])
                  setSearch('')
                }}
              >
                {t.name} <span className="text-slate-500">{[t.phone, t.email].filter(Boolean).join(' · ')}</span>
              </button>
            </li>
          ))}
        </ul>
      )}
      {term && results.data && options.length === 0 && (
        <p className="mt-1 text-sm text-slate-500">
          No other tenants match. <Link to="/app/tenants" className="text-brand-700 hover:underline">Add the tenant first</Link>.
        </p>
      )}
    </div>
  )
}
