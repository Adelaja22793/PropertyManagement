import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, post, put } from '../../lib/api'
import type { Guarantor } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Badge, Button, Card, Empty, ErrorBox, TextField } from '../../components/ui'
import { ExportButton } from '../exports/ExportButton'

export function GuarantorsPanel({ tenantId }: { tenantId: string }) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const canEdit = can(Permissions.tenantsUpdate)
  const canExport = can(Permissions.dataExport)
  const [editing, setEditing] = useState<Guarantor | 'new' | null>(null)
  const guarantors = useQuery({
    queryKey: ['tenants', tenantId, 'guarantors'],
    queryFn: () => get<Guarantor[]>(`/api/v1/tenants/${tenantId}/guarantors`),
  })
  const remove = useMutation({
    mutationFn: (g: Guarantor) => del(`/api/v1/tenants/${tenantId}/guarantors/${g.id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['tenants'] }),
  })

  return (
    <Card
      title="Guarantors"
      actions={
        canEdit &&
        editing === null && (
          <Button variant="secondary" onClick={() => setEditing('new')}>
            Add guarantor
          </Button>
        )
      }
    >
      {editing !== null && (
        <GuarantorForm tenantId={tenantId} guarantor={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />
      )}
      <ErrorBox error={guarantors.error ?? remove.error} />
      {guarantors.data?.length === 0 && <Empty>No guarantors yet.</Empty>}
      <ul className="divide-y divide-slate-100">
        {guarantors.data?.map((g) => (
          <li key={g.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
            <div className="min-w-0 text-sm">
              <p className="font-medium">
                {g.fullName} {g.relationship && <span className="font-normal text-slate-500">· {g.relationship}</span>}
              </p>
              <p className="break-words text-slate-600">{[g.phone, g.email].filter(Boolean).join(' · ')}</p>
              {(g.employer || g.occupation) && (
                <p className="text-xs text-slate-500">{[g.occupation, g.employer].filter(Boolean).join(' at ')}</p>
              )}
              {g.address && <p className="text-xs text-slate-500">{g.address}</p>}
              <div className="mt-1">
                {g.signedFormReceived ? <Badge tone="green">Signed form received</Badge> : <Badge tone="amber">Form not received</Badge>}
              </div>
            </div>
            {editing === null && (canEdit || canExport) && (
              <div className="flex flex-wrap items-start gap-1">
                {canExport && (
                  <ExportButton
                    path={`/api/v1/exports/guarantors/${g.id}`}
                    fallbackName="guarantor-data.zip"
                    label="Download data"
                    ariaLabel={`Download ${g.fullName}'s data`}
                    variant="ghost"
                  />
                )}
                {canEdit && (
                  <>
                    <Button variant="ghost" aria-label={`Edit ${g.fullName}`} onClick={() => setEditing(g)}>
                      Edit
                    </Button>
                    <Button
                      variant="ghost"
                      aria-label={`Remove ${g.fullName}`}
                      onClick={() => {
                        if (confirm(`Remove ${g.fullName} as a guarantor?`)) remove.mutate(g)
                      }}
                    >
                      Remove
                    </Button>
                  </>
                )}
              </div>
            )}
          </li>
        ))}
      </ul>
    </Card>
  )
}

const schema = z
  .object({
    fullName: z.string().trim().min(2, 'Enter the guarantor’s full name.').max(200),
    phone: optionalText(30),
    email: optionalEmail,
    address: optionalText(500),
    relationship: optionalText(100),
    employer: optionalText(200),
    occupation: optionalText(100),
    signedFormReceived: z.boolean(),
  })
  .refine((v) => v.phone.trim() !== '' || v.email.trim() !== '', {
    message: 'Enter a phone number or email for the guarantor.',
    path: ['phone'],
  })
type Values = z.infer<typeof schema>

function GuarantorForm({ tenantId, guarantor, onDone }: { tenantId: string; guarantor: Guarantor | null; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: guarantor?.fullName ?? '',
      phone: guarantor?.phone ?? '',
      email: guarantor?.email ?? '',
      address: guarantor?.address ?? '',
      relationship: guarantor?.relationship ?? '',
      employer: guarantor?.employer ?? '',
      occupation: guarantor?.occupation ?? '',
      signedFormReceived: guarantor?.signedFormReceived ?? false,
    },
  })
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        fullName: v.fullName.trim(),
        phone: blankToNull(v.phone),
        email: blankToNull(v.email),
        address: blankToNull(v.address),
        relationship: blankToNull(v.relationship),
        employer: blankToNull(v.employer),
        occupation: blankToNull(v.occupation),
        signedFormReceived: v.signedFormReceived,
      }
      return guarantor
        ? put<Guarantor>(`/api/v1/tenants/${tenantId}/guarantors/${guarantor.id}`, body)
        : post<Guarantor>(`/api/v1/tenants/${tenantId}/guarantors`, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      onDone()
    },
  })
  const errors = formState.errors
  return (
    <form
      className="mb-4 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200"
      onSubmit={handleSubmit((v) => save.mutate(v))}
      noValidate
    >
      <h3 className="text-sm font-semibold">{guarantor ? `Edit ${guarantor.fullName}` : 'New guarantor'}</h3>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label="Full name" id="guarantor-name" {...register('fullName')} error={errors.fullName?.message} />
        <TextField label="Relationship" id="guarantor-relationship" {...register('relationship')} error={errors.relationship?.message} />
        <TextField label="Phone" id="guarantor-phone" type="tel" {...register('phone')} error={errors.phone?.message} />
        <TextField label="Email" id="guarantor-email" type="email" {...register('email')} error={errors.email?.message} />
        <TextField label="Employer" id="guarantor-employer" {...register('employer')} error={errors.employer?.message} />
        <TextField label="Occupation" id="guarantor-occupation" {...register('occupation')} error={errors.occupation?.message} />
        <div className="sm:col-span-2">
          <TextField label="Address" id="guarantor-address" {...register('address')} error={errors.address?.message} />
        </div>
      </div>
      <label className="flex items-center gap-2 text-sm text-slate-700">
        <input type="checkbox" {...register('signedFormReceived')} /> Signed guarantor form received
      </label>
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save guarantor
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
