import { useEffect } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, put } from '../../lib/api'
import type { OrganizationDetail } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { Button, Card, ErrorBox, PageHeader, TextField } from '../../components/ui'

const optional = (max: number) => z.string().trim().max(max)
const schema = z.object({
  name: z.string().trim().min(2, 'Enter the organization name.').max(200),
  contactEmail: z.union([z.literal(''), z.email('Enter a valid email address.')]),
  contactPhone: optional(30),
  address: optional(500),
  taxIdentificationNumber: optional(30),
  vatNumber: optional(30),
  timeZone: z.string().trim().min(1),
})
type Values = z.infer<typeof schema>

export function SettingsPage() {
  const { organization, can, refresh } = useSession()
  const canEdit = can(Permissions.settingsManage)
  const queryClient = useQueryClient()
  const path = `/api/v1/organizations/${organization!.id}`
  const detail = useQuery({ queryKey: ['organization', organization!.id], queryFn: () => get<OrganizationDetail>(path) })
  const { register, handleSubmit, formState, reset } = useForm<Values>({ resolver: zodResolver(schema) })

  useEffect(() => {
    if (!detail.data) return
    const d = detail.data
    reset({
      name: d.name,
      contactEmail: d.contactEmail ?? '',
      contactPhone: d.contactPhone ?? '',
      address: d.address ?? '',
      taxIdentificationNumber: d.taxIdentificationNumber ?? '',
      vatNumber: d.vatNumber ?? '',
      timeZone: d.timeZone,
    })
  }, [detail.data, reset])

  const save = useMutation({
    mutationFn: (v: Values) => put<OrganizationDetail>(path, v),
    onSuccess: async (updated) => {
      queryClient.setQueryData(['organization', organization!.id], updated)
      await refresh()
    },
  })

  return (
    <>
      <PageHeader title="Settings" description="Your organization's profile. Tax numbers appear on invoices and receipts in later phases." />
      <Card>
        <ErrorBox error={detail.error ?? save.error} />
        {detail.data && (
          <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
            <fieldset disabled={!canEdit} className="grid gap-4 sm:grid-cols-2">
              <div className="sm:col-span-2">
                <TextField label="Name" {...register('name')} error={formState.errors.name?.message} />
              </div>
              <TextField label="Contact email" type="email" {...register('contactEmail')} error={formState.errors.contactEmail?.message} />
              <TextField label="Contact phone" {...register('contactPhone')} error={formState.errors.contactPhone?.message} />
              <div className="sm:col-span-2">
                <TextField label="Address" {...register('address')} error={formState.errors.address?.message} />
              </div>
              <TextField label="Tax Identification Number (TIN)" {...register('taxIdentificationNumber')} />
              <TextField label="VAT number" {...register('vatNumber')} />
              <TextField label="Time zone" {...register('timeZone')} hint="Africa/Lagos for Nigeria." />
              <TextField label="Currency" value={detail.data.baseCurrency} readOnly disabled hint="Naira is the default currency." />
            </fieldset>
            {canEdit && (
              <div className="flex items-center gap-3">
                <Button type="submit" disabled={save.isPending || !formState.isDirty}>
                  Save changes
                </Button>
                {save.isSuccess && !formState.isDirty && <span className="text-sm text-brand-700">Saved.</span>}
              </div>
            )}
          </form>
        )}
      </Card>
    </>
  )
}
