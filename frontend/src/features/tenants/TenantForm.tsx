import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { post, put } from '../../lib/api'
import type { TenantDetail } from '../../lib/types'
import { PartyTypes } from '../../lib/types'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Button, Card, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

const schema = z
  .object({
    type: z.enum(PartyTypes),
    name: z.string().trim().min(2, 'Enter the tenant’s name.').max(200),
    email: optionalEmail,
    phone: optionalText(30),
    alternatePhone: optionalText(30),
    address: optionalText(500),
    employer: optionalText(200),
    occupation: optionalText(100),
    emergencyContactName: optionalText(200),
    emergencyContactPhone: optionalText(30),
    companyRegistrationNumber: optionalText(30),
    taxIdentificationNumber: optionalText(30),
    notes: optionalText(2000),
  })
  .refine((v) => v.email.trim() !== '' || v.phone.trim() !== '', { message: 'Enter an email address or a phone number.', path: ['phone'] })
type Values = z.infer<typeof schema>

export function TenantForm({ tenant, onDone }: { tenant: TenantDetail | null; onDone: (saved?: TenantDetail) => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: tenant?.type ?? 'Individual',
      name: tenant?.name ?? '',
      email: tenant?.email ?? '',
      phone: tenant?.phone ?? '',
      alternatePhone: tenant?.alternatePhone ?? '',
      address: tenant?.address ?? '',
      employer: tenant?.employer ?? '',
      occupation: tenant?.occupation ?? '',
      emergencyContactName: tenant?.emergencyContactName ?? '',
      emergencyContactPhone: tenant?.emergencyContactPhone ?? '',
      companyRegistrationNumber: tenant?.companyRegistrationNumber ?? '',
      taxIdentificationNumber: tenant?.taxIdentificationNumber ?? '',
      notes: tenant?.notes ?? '',
    },
  })
  const errors = formState.errors
  const company = useWatch({ control, name: 'type' }) === 'Company'
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        type: v.type,
        name: v.name.trim(),
        email: blankToNull(v.email),
        phone: blankToNull(v.phone),
        alternatePhone: blankToNull(v.alternatePhone),
        address: blankToNull(v.address),
        employer: company ? null : blankToNull(v.employer),
        occupation: company ? null : blankToNull(v.occupation),
        emergencyContactName: blankToNull(v.emergencyContactName),
        emergencyContactPhone: blankToNull(v.emergencyContactPhone),
        companyRegistrationNumber: company ? blankToNull(v.companyRegistrationNumber) : null,
        taxIdentificationNumber: company ? blankToNull(v.taxIdentificationNumber) : null,
        notes: blankToNull(v.notes),
      }
      return tenant ? put<TenantDetail>(`/api/v1/tenants/${tenant.id}`, body) : post<TenantDetail>('/api/v1/tenants', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      onDone(saved)
    },
  })

  return (
    <div className="mb-6">
      <Card title={tenant ? `Edit ${tenant.name}` : 'New tenant'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Tenant type" {...register('type')}>
              {PartyTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </SelectField>
            <TextField label={company ? 'Company name' : 'Full name'} id="name" {...register('name')} error={errors.name?.message} />
            <TextField label="Email" type="email" {...register('email')} error={errors.email?.message} />
            <TextField label="Phone" type="tel" {...register('phone')} error={errors.phone?.message} />
            <TextField label="Alternate phone" type="tel" {...register('alternatePhone')} error={errors.alternatePhone?.message} />
            <TextField label="Address" {...register('address')} error={errors.address?.message} />
            {company ? (
              <>
                <TextField label="RC number" hint="CAC registration number." {...register('companyRegistrationNumber')} error={errors.companyRegistrationNumber?.message} />
                <TextField label="TIN" {...register('taxIdentificationNumber')} error={errors.taxIdentificationNumber?.message} />
              </>
            ) : (
              <>
                <TextField label="Employer" {...register('employer')} error={errors.employer?.message} />
                <TextField label="Occupation" {...register('occupation')} error={errors.occupation?.message} />
              </>
            )}
            <TextField label="Emergency contact name" {...register('emergencyContactName')} error={errors.emergencyContactName?.message} />
            <TextField label="Emergency contact phone" type="tel" {...register('emergencyContactPhone')} error={errors.emergencyContactPhone?.message} />
          </div>
          <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
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
