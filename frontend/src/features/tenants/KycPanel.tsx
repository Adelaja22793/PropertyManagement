import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { get, put } from '../../lib/api'
import type { KycDetail, KycSummary, VerificationStatus } from '../../lib/types'
import { IdentityDocumentTypes, VerificationStatuses } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, DetailList, ErrorBox, SelectField, TextField } from '../../components/ui'

/**
 * Identity details. Everyone who can see the tenant sees the ID type, last four digits and status; the full numbers
 * need Tenants.ViewKyc and each reveal is audited by the server.
 */
export function KycPanel({ tenantId, kyc }: { tenantId: string; kyc: KycSummary }) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const canReveal = can(Permissions.tenantsViewKyc)
  const canEdit = can(Permissions.tenantsUpdate)
  const [full, setFull] = useState<KycDetail | null>(null)
  const [editing, setEditing] = useState(false)
  const reveal = useMutation({
    mutationFn: () => get<KycDetail>(`/api/v1/tenants/${tenantId}/kyc`),
    onSuccess: setFull,
  })
  const verify = useMutation({
    mutationFn: (status: VerificationStatus) => put<KycSummary>(`/api/v1/tenants/${tenantId}/kyc/verification`, { status }),
    onSuccess: async () => {
      setFull(null)
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
    },
  })
  const masked = (last4: string | null) => (last4 ? `•••• ${last4}` : null)

  return (
    <Card
      title="Identity (KYC)"
      actions={
        canEdit &&
        !editing && (
          <Button variant="secondary" onClick={() => setEditing(true)}>
            {kyc.idType ? 'Update ID' : 'Add ID'}
          </Button>
        )
      }
    >
      {editing ? (
        <KycForm
          tenantId={tenantId}
          current={full ?? { idType: kyc.idType, idNumber: '', bvn: '' }}
          onDone={() => {
            setEditing(false)
            setFull(null)
          }}
        />
      ) : (
        <div className="space-y-4">
          <div className="flex flex-wrap items-center gap-2">
            <Badge tone={statusTone(kyc.status)}>{kyc.status}</Badge>
            {kyc.verifiedAtUtc && (
              <span className="text-xs text-slate-500">
                by {kyc.verifiedByName ?? 'someone'} {formatDate(kyc.verifiedAtUtc)}
              </span>
            )}
          </div>
          <DetailList
            items={[
              { label: 'ID type', value: kyc.idType ? enumLabel(kyc.idType) : 'Not recorded' },
              { label: 'ID number', value: full ? full.idNumber : masked(kyc.idNumberLast4) },
              { label: 'BVN', value: full ? full.bvn : masked(kyc.bvnLast4) },
            ]}
          />
          <ErrorBox error={reveal.error ?? verify.error} />
          <div className="flex flex-wrap items-center gap-2">
            {canReveal &&
              (kyc.idNumberLast4 || kyc.bvnLast4) &&
              (full ? (
                <Button variant="secondary" onClick={() => setFull(null)}>
                  Hide full details
                </Button>
              ) : (
                <Button variant="secondary" disabled={reveal.isPending} onClick={() => reveal.mutate()}>
                  Show full details
                </Button>
              ))}
            {canReveal && kyc.idType && (
              <div className="flex items-center gap-2">
                <label htmlFor="kyc-status" className="text-sm text-slate-700">
                  Verification
                </label>
                <select
                  id="kyc-status"
                  value={kyc.status}
                  disabled={verify.isPending}
                  onChange={(e) => verify.mutate(e.target.value as VerificationStatus)}
                  className="rounded-md border-0 py-1.5 text-sm ring-1 ring-slate-300"
                >
                  {VerificationStatuses.map((s) => (
                    <option key={s} value={s}>
                      {s}
                    </option>
                  ))}
                </select>
              </div>
            )}
          </div>
          {canReveal && full && <p className="text-xs text-slate-500">Viewing full ID numbers is recorded in the audit log.</p>}
        </div>
      )}
    </Card>
  )
}

const elevenDigits = /^\d{11}$/
const schema = z
  .object({
    idType: z.union([z.literal(''), z.enum(IdentityDocumentTypes)]),
    idNumber: z.string().trim().max(30),
    bvn: z
      .string()
      .trim()
      .refine((v) => v === '' || elevenDigits.test(v), 'A BVN is 11 digits.'),
  })
  .refine((v) => (v.idType === '') === (v.idNumber === ''), { message: 'Enter both the ID type and the ID number.', path: ['idNumber'] })
  .refine((v) => v.idType !== 'Nin' || elevenDigits.test(v.idNumber), { message: 'A NIN is 11 digits.', path: ['idNumber'] })
type Values = z.infer<typeof schema>

function KycForm({
  tenantId,
  current,
  onDone,
}: {
  tenantId: string
  current: { idType: KycDetail['idType']; idNumber: string | null; bvn: string | null }
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { idType: current.idType ?? '', idNumber: current.idNumber ?? '', bvn: current.bvn ?? '' },
  })
  const save = useMutation({
    mutationFn: (v: Values) =>
      put<KycSummary>(`/api/v1/tenants/${tenantId}/kyc`, {
        idType: v.idType || null,
        idNumber: v.idNumber.trim() || null,
        bvn: v.bvn.trim() || null,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenants'] })
      onDone()
    },
  })
  const errors = formState.errors
  return (
    <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
      <p className="text-sm text-slate-600">Saving replaces the ID number and BVN on file and resets verification. Enter both again if you're changing one.</p>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-3">
        <SelectField label="ID type" {...register('idType')}>
          <option value="">None</option>
          {IdentityDocumentTypes.map((t) => (
            <option key={t} value={t}>
              {enumLabel(t)}
            </option>
          ))}
        </SelectField>
        <TextField label="ID number" autoComplete="off" {...register('idNumber')} error={errors.idNumber?.message} />
        <TextField label="BVN" inputMode="numeric" autoComplete="off" maxLength={11} {...register('bvn')} error={errors.bvn?.message} />
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save ID
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
