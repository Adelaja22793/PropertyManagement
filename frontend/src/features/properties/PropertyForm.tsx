import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Branch, PropertyDetail } from '../../lib/types'
import { PropertyTypes } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { NigerianStates } from '../../lib/nigeria'
import { blankToNull, optionalText } from '../../lib/validation'
import { enumLabel } from '../../lib/format'
import { Button, Card, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

const schema = z.object({
  code: z.string().trim().min(1, 'Enter a short code, e.g. LKK-01.').max(30, 'Use at most 30 characters.'),
  name: z.string().trim().min(2, 'Enter the property name.').max(200),
  type: z.enum(PropertyTypes),
  status: z.enum(['Active', 'UnderRenovation']),
  addressLine: z.string().trim().min(3, 'Enter the street address.').max(300),
  city: optionalText(100),
  state: z.string().min(1, 'Choose a state.'),
  localGovernmentArea: optionalText(100),
  branchId: z.string(),
  managerUserId: z.string(),
  amenities: optionalText(1000),
  description: optionalText(2000),
})
type Values = z.infer<typeof schema>

/** Creates a property, or edits one when `property` is given. */
export function PropertyForm({ property, onDone }: { property: PropertyDetail | null; onDone: (saved?: PropertyDetail) => void }) {
  const { organization, can } = useSession()
  const queryClient = useQueryClient()
  const branches = useQuery({
    queryKey: ['branches'],
    queryFn: () => get<Branch[]>('/api/v1/branches'),
    enabled: can(Permissions.branchesView),
  })
  const canPickManager = can(Permissions.propertiesUpdate)
  const managerOptions = useQuery({
    queryKey: ['manager-options', organization!.id],
    queryFn: () => get<{ userId: string; fullName: string }[]>('/api/v1/properties/manager-options'),
    enabled: canPickManager,
  })
  const staff = managerOptions.data ?? []

  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: property?.code ?? '',
      name: property?.name ?? '',
      type: property?.type ?? 'Residential',
      status: property?.status === 'UnderRenovation' ? 'UnderRenovation' : 'Active',
      addressLine: property?.addressLine ?? '',
      city: property?.city ?? '',
      state: property?.state ?? 'Lagos',
      localGovernmentArea: property?.localGovernmentArea ?? '',
      branchId: property?.branchId ?? '',
      managerUserId: property?.managerUserId ?? '',
      amenities: property?.amenities ?? '',
      description: property?.description ?? '',
    },
  })
  const errors = formState.errors

  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        code: v.code.trim(),
        name: v.name.trim(),
        type: v.type,
        status: v.status,
        description: blankToNull(v.description),
        addressLine: v.addressLine.trim(),
        city: blankToNull(v.city),
        state: v.state,
        localGovernmentArea: blankToNull(v.localGovernmentArea),
        branchId: v.branchId || null,
        managerUserId: v.managerUserId || null,
        amenities: blankToNull(v.amenities),
      }
      return property ? put<PropertyDetail>(`/api/v1/properties/${property.id}`, body) : post<PropertyDetail>('/api/v1/properties', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['properties'] })
      onDone(saved)
    },
  })

  return (
    <div className="mb-6">
      <Card title={property ? `Edit ${property.name}` : 'New property'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Code" hint="A short reference, e.g. LKK-01." {...register('code')} error={errors.code?.message} />
            <TextField label="Name" {...register('name')} error={errors.name?.message} />
            <SelectField label="Type" {...register('type')}>
              {PropertyTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </SelectField>
            {property && (
              <SelectField label="Status" {...register('status')}>
                <option value="Active">Active</option>
                <option value="UnderRenovation">{enumLabel('UnderRenovation')}</option>
              </SelectField>
            )}
            <div className="sm:col-span-2">
              <TextField label="Address" {...register('addressLine')} error={errors.addressLine?.message} />
            </div>
            <TextField label="City" {...register('city')} error={errors.city?.message} />
            <SelectField label="State" {...register('state')} error={errors.state?.message}>
              {NigerianStates.map((s) => (
                <option key={s} value={s}>
                  {s === 'FCT' ? 'FCT (Abuja)' : s}
                </option>
              ))}
            </SelectField>
            <TextField label="LGA" {...register('localGovernmentArea')} error={errors.localGovernmentArea?.message} />
            {(branches.data?.length ?? 0) > 0 && (
              <SelectField label="Branch" {...register('branchId')}>
                <option value="">No branch</option>
                {branches.data!.map((b) => (
                  <option key={b.id} value={b.id}>
                    {b.name}
                  </option>
                ))}
              </SelectField>
            )}
            {canPickManager && (
              <SelectField label="Property manager" {...register('managerUserId')}>
                <option value="">Not assigned</option>
                {staff.map((m) => (
                  <option key={m.userId} value={m.userId}>
                    {m.fullName}
                  </option>
                ))}
              </SelectField>
            )}
          </div>
          <TextField label="Amenities" hint="Comma separated, e.g. borehole, generator, security." {...register('amenities')} error={errors.amenities?.message} />
          <TextAreaField label="Description" {...register('description')} error={errors.description?.message} />
          <div className="flex gap-2">
            <Button type="submit" disabled={save.isPending}>
              Save
            </Button>
            <Button type="button" variant="secondary" onClick={() => onDone()}>
              Cancel
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
