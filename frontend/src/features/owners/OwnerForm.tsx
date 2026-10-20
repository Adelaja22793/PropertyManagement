import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { post, put } from '../../lib/api'
import type { OwnerDetail } from '../../lib/types'
import { ContactChannels, PartyTypes } from '../../lib/types'
import { enumLabel } from '../../lib/format'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Button, Card, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

const schema = z
  .object({
    type: z.enum(PartyTypes),
    name: z.string().trim().min(2, 'Enter the owner’s name.').max(200),
    email: optionalEmail,
    phone: optionalText(30),
    address: optionalText(500),
    taxIdentificationNumber: optionalText(30),
    preferredChannel: z.enum(ContactChannels),
    notes: optionalText(2000),
  })
  .refine((v) => v.email.trim() !== '' || v.phone.trim() !== '', { message: 'Enter an email address or a phone number.', path: ['phone'] })
type Values = z.infer<typeof schema>

export function OwnerForm({ owner, onDone }: { owner: OwnerDetail | null; onDone: (saved?: OwnerDetail) => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      type: owner?.type ?? 'Individual',
      name: owner?.name ?? '',
      email: owner?.email ?? '',
      phone: owner?.phone ?? '',
      address: owner?.address ?? '',
      taxIdentificationNumber: owner?.taxIdentificationNumber ?? '',
      preferredChannel: owner?.preferredChannel ?? 'Email',
      notes: owner?.notes ?? '',
    },
  })
  const errors = formState.errors
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        type: v.type,
        name: v.name.trim(),
        email: blankToNull(v.email),
        phone: blankToNull(v.phone),
        address: blankToNull(v.address),
        taxIdentificationNumber: blankToNull(v.taxIdentificationNumber),
        preferredChannel: v.preferredChannel,
        notes: blankToNull(v.notes),
      }
      return owner ? put<OwnerDetail>(`/api/v1/owners/${owner.id}`, body) : post<OwnerDetail>('/api/v1/owners', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['owners'] })
      onDone(saved)
    },
  })

  return (
    <div className="mb-6">
      <Card title={owner ? `Edit ${owner.name}` : 'New owner'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField label="Owner type" {...register('type')}>
              {PartyTypes.map((t) => (
                <option key={t} value={t}>
                  {t}
                </option>
              ))}
            </SelectField>
            <TextField label="Name" {...register('name')} error={errors.name?.message} />
            <TextField label="Email" type="email" {...register('email')} error={errors.email?.message} />
            <TextField label="Phone" type="tel" {...register('phone')} error={errors.phone?.message} />
            <div className="sm:col-span-2">
              <TextField label="Address" {...register('address')} error={errors.address?.message} />
            </div>
            <TextField label="TIN" hint="Tax identification number (optional)." {...register('taxIdentificationNumber')} error={errors.taxIdentificationNumber?.message} />
            <SelectField label="Preferred channel" {...register('preferredChannel')}>
              {ContactChannels.map((c) => (
                <option key={c} value={c}>
                  {enumLabel(c)}
                </option>
              ))}
            </SelectField>
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
