import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { ScreeningCheck } from '../../lib/types'
import { ScreeningCheckTypes, ScreeningOutcomes } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, optionalText } from '../../lib/validation'
import { Badge, Button, Card, Empty, ErrorBox, SelectField, TextAreaField } from '../../components/ui'

export function ScreeningPanel({ tenantId }: { tenantId: string }) {
  const { can } = useSession()
  const canAdd = can(Permissions.tenantsUpdate)
  const [adding, setAdding] = useState(false)
  const checks = useQuery({
    queryKey: ['tenants', tenantId, 'screening'],
    queryFn: () => get<ScreeningCheck[]>(`/api/v1/tenants/${tenantId}/screening`),
  })

  return (
    <Card
      title="Screening checks"
      actions={
        canAdd &&
        !adding && (
          <Button variant="secondary" onClick={() => setAdding(true)}>
            Record check
          </Button>
        )
      }
    >
      {adding && <ScreeningForm tenantId={tenantId} onDone={() => setAdding(false)} />}
      <ErrorBox error={checks.error} />
      {checks.data?.length === 0 && <Empty>No checks recorded yet.</Empty>}
      <ul className="divide-y divide-slate-100">
        {checks.data?.map((c) => (
          <li key={c.id} className="flex flex-wrap items-start justify-between gap-3 py-3 text-sm">
            <div className="min-w-0">
              <p className="font-medium">{enumLabel(c.type)}</p>
              {c.notes && <p className="break-words text-slate-600">{c.notes}</p>}
              <p className="text-xs text-slate-500">
                {c.checkedByName ?? 'Someone'} · {formatDate(c.checkedAtUtc)}
              </p>
            </div>
            <Badge tone={statusTone(c.outcome)}>{c.outcome}</Badge>
          </li>
        ))}
      </ul>
    </Card>
  )
}

const schema = z.object({
  type: z.enum(ScreeningCheckTypes),
  outcome: z.enum(ScreeningOutcomes),
  notes: optionalText(2000),
})
type Values = z.infer<typeof schema>

function ScreeningForm({ tenantId, onDone }: { tenantId: string; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { type: 'Employer', outcome: 'Pending', notes: '' },
  })
  const add = useMutation({
    mutationFn: (v: Values) => post<ScreeningCheck>(`/api/v1/tenants/${tenantId}/screening`, { ...v, notes: blankToNull(v.notes) }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['tenants', tenantId, 'screening'] })
      onDone()
    },
  })
  return (
    <form className="mb-4 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => add.mutate(v))} noValidate>
      <ErrorBox error={add.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField label="Check" id="screening-type" {...register('type')}>
          {ScreeningCheckTypes.map((t) => (
            <option key={t} value={t}>
              {enumLabel(t)}
            </option>
          ))}
        </SelectField>
        <SelectField label="Outcome" id="screening-outcome" {...register('outcome')}>
          {ScreeningOutcomes.map((o) => (
            <option key={o} value={o}>
              {o}
            </option>
          ))}
        </SelectField>
      </div>
      <TextAreaField label="Notes" id="screening-notes" rows={2} {...register('notes')} error={formState.errors.notes?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={add.isPending}>
          Save check
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
