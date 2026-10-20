import { useEffect } from 'react'
import { Link, useNavigate, useParams, useSearchParams } from 'react-router-dom'
import { useForm, useWatch, type UseFormGetValues, type UseFormSetValue } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { ListingAgent, ListingDetail, Paged, PropertySummary, Unit } from '../../lib/types'
import { BillingFrequencies } from '../../lib/types'
import { enumLabel } from '../../lib/format'
import { blankToNull, optionalText, requiredAmount } from '../../lib/validation'
import { useSession } from '../../app/session'
import { Button, Card, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { unitSummary } from './listingFormat'

const schema = z.object({
  propertyId: z.string().min(1, 'Choose a property.'),
  unitId: z.string().min(1, 'Choose a unit.'),
  headline: z.string().trim().min(5, 'Write a short headline.').max(120),
  description: optionalText(4000),
  askingRent: requiredAmount.refine((v) => Number(v) > 0, 'Enter the asking rent.'),
  billingFrequency: z.enum(BillingFrequencies),
  availableFrom: z.string(),
  features: optionalText(2000),
  showStreetAddress: z.boolean(),
  contactName: optionalText(200),
  contactPhone: optionalText(30),
  agentUserId: z.string(),
})
type Values = z.infer<typeof schema>

/** Members whose only listing role is Agent work on their own listings and can't reassign them. */
function useAgentOnly() {
  const { organization } = useSession()
  return (
    !!organization && organization.roles.length > 0 && organization.roles.every((r) => r === 'Agent' || r === 'Owner' || r === 'Tenant')
  )
}

/** Selects the unit and fills in its default rent and a headline when those are still blank. */
function fillFromUnit(unit: Unit | undefined, setValue: UseFormSetValue<Values>, getValues: UseFormGetValues<Values>) {
  if (!unit) return
  setValue('unitId', unit.id)
  if (!getValues('askingRent')) {
    setValue('askingRent', unit.defaultRent.toString())
    setValue('billingFrequency', unit.billingFrequency)
  }
  if (!getValues('headline')) setValue('headline', unitSummary(unit.type, unit.bedrooms, unit.bathrooms))
}

export function ListingEditorPage() {
  const { id } = useParams()
  const listing = useQuery({ queryKey: ['listings', id], queryFn: () => get<ListingDetail>(`/api/v1/listings/${id}`), enabled: !!id })
  if (id && !listing.data) return <ErrorBox error={listing.error} />
  return <ListingEditor key={listing.data?.id ?? 'new'} listing={listing.data ?? null} />
}

function ListingEditor({ listing }: { listing: ListingDetail | null }) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [params] = useSearchParams()
  const agentOnly = useAgentOnly()
  const { register, handleSubmit, formState, control, setValue, getValues } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      propertyId: listing?.propertyId ?? params.get('propertyId') ?? '',
      unitId: listing?.unitId ?? params.get('unitId') ?? '',
      headline: listing?.headline ?? '',
      description: listing?.description ?? '',
      askingRent: listing?.askingRent.toString() ?? '',
      billingFrequency: listing?.billingFrequency ?? 'Annual',
      availableFrom: listing?.availableFrom ?? '',
      features: listing?.features ?? '',
      showStreetAddress: listing?.showStreetAddress ?? false,
      contactName: listing?.contactName ?? '',
      contactPhone: listing?.contactPhone ?? '',
      agentUserId: listing?.agentUserId ?? '',
    },
  })
  const errors = formState.errors
  const propertyId = useWatch({ control, name: 'propertyId' })

  const properties = useQuery({
    queryKey: ['properties', 'options'],
    queryFn: () => get<Paged<PropertySummary>>('/api/v1/properties?pageSize=100'),
    enabled: !listing,
  })
  const units = useQuery({
    queryKey: ['properties', propertyId, 'units', false],
    queryFn: () => get<Unit[]>(`/api/v1/properties/${propertyId}/units`),
    enabled: !listing && !!propertyId,
  })
  const agents = useQuery({
    queryKey: ['listings', 'agents'],
    queryFn: () => get<ListingAgent[]>('/api/v1/listings/agents'),
    enabled: !agentOnly,
  })

  const presetUnit = listing ? null : params.get('unitId')
  useEffect(() => {
    const unit = units.data?.find((u) => u.id === presetUnit)
    // The options arrive after the form mounts, so select the unit again once they are there.
    if (unit) fillFromUnit(unit, setValue, getValues)
  }, [units.data, presetUnit, setValue, getValues])

  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        unitId: v.unitId,
        headline: v.headline.trim(),
        description: blankToNull(v.description),
        askingRent: Number(v.askingRent),
        currency: 'NGN',
        billingFrequency: v.billingFrequency,
        availableFrom: v.availableFrom || null,
        features: blankToNull(v.features),
        showStreetAddress: v.showStreetAddress,
        contactName: blankToNull(v.contactName),
        contactPhone: blankToNull(v.contactPhone),
        agentUserId: agentOnly ? (listing?.agentUserId ?? null) : v.agentUserId || null,
      }
      return listing ? put<ListingDetail>(`/api/v1/listings/${listing.id}`, body) : post<ListingDetail>('/api/v1/listings', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['listings'] })
      navigate(`/app/listings/${saved.id}`)
    },
  })

  return (
    <>
      <Link
        to={listing ? `/app/listings/${listing.id}` : '/app/listings'}
        className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline"
      >
        ← {listing ? listing.headline : 'Listings'}
      </Link>
      <PageHeader
        title={listing ? 'Edit listing' : 'New listing'}
        description={listing ? undefined : 'Saved as a draft. Add photos, then publish it to the public page.'}
      />
      <form className="space-y-6" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
        <ErrorBox error={save.error} />
        <Card title="Unit">
          {listing ? (
            <p className="text-sm text-slate-700">
              {listing.propertyName}, unit {listing.unitCode}
            </p>
          ) : (
            <div className="grid gap-4 sm:grid-cols-2">
              <SelectField
                label="Property"
                {...register('propertyId', { onChange: () => setValue('unitId', '') })}
                error={errors.propertyId?.message}
              >
                <option value="">Choose a property…</option>
                {properties.data?.items
                  .filter((p) => p.status !== 'Archived')
                  .map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({p.code})
                    </option>
                  ))}
              </SelectField>
              <SelectField
                label="Unit"
                {...register('unitId', {
                  onChange: (e) =>
                    fillFromUnit(
                      units.data?.find((u) => u.id === e.target.value),
                      setValue,
                      getValues,
                    ),
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

        <Card title="What the public sees">
          <div className="grid gap-4 sm:grid-cols-2">
            <div className="sm:col-span-2">
              <TextField
                label="Headline"
                placeholder="Bright 2-bedroom flat in Lekki Phase 1"
                {...register('headline')}
                error={errors.headline?.message}
              />
            </div>
            <div className="sm:col-span-2">
              <TextAreaField
                label="Description"
                rows={5}
                hint="Needed before you publish."
                {...register('description')}
                error={errors.description?.message}
              />
            </div>
            <TextField label="Asking rent (₦)" inputMode="decimal" {...register('askingRent')} error={errors.askingRent?.message} />
            <SelectField label="Rent is paid" {...register('billingFrequency')}>
              {BillingFrequencies.map((f) => (
                <option key={f} value={f}>
                  {enumLabel(f)}
                </option>
              ))}
            </SelectField>
            <TextField label="Available from" type="date" hint="Leave blank if it's available now." {...register('availableFrom')} />
            <TextAreaField
              label="Features"
              rows={3}
              hint="One per line, such as Prepaid meter."
              {...register('features')}
              error={errors.features?.message}
            />
            <label className="flex items-center gap-2 text-sm text-slate-700 sm:col-span-2">
              <input type="checkbox" {...register('showStreetAddress')} />
              Show the street address (otherwise only the area and state are shown)
            </label>
          </div>
        </Card>

        <Card title="Who handles enquiries">
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Contact name on the listing" {...register('contactName')} error={errors.contactName?.message} />
            <TextField
              label="Contact phone on the listing"
              type="tel"
              hint="Leave blank to show the company's phone."
              {...register('contactPhone')}
              error={errors.contactPhone?.message}
            />
            {!agentOnly && (
              <SelectField
                label="Agent"
                hint="New enquiries are emailed to the agent, or the property manager if none."
                {...register('agentUserId')}
              >
                <option value="">No agent</option>
                {agents.data?.map((a) => (
                  <option key={a.userId} value={a.userId}>
                    {a.fullName}
                  </option>
                ))}
              </SelectField>
            )}
          </div>
        </Card>

        <div className="flex gap-2">
          <Button type="submit" disabled={save.isPending}>
            {save.isPending ? 'Saving…' : listing ? 'Save changes' : 'Create listing'}
          </Button>
          <Button type="button" variant="secondary" onClick={() => navigate(listing ? `/app/listings/${listing.id}` : '/app/listings')}>
            Cancel
          </Button>
        </div>
      </form>
    </>
  )
}
