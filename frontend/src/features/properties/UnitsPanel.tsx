import { useState } from 'react'
import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Unit } from '../../lib/types'
import { BillingFrequencies, OccupancyStatuses, UnitTypes } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatNaira } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, numberOrNull, optionalAmount, optionalText, optionalWhole, requiredAmount } from '../../lib/validation'
import { Badge, Button, Card, Empty, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

export function UnitsPanel({ propertyId, archived }: { propertyId: string; archived: boolean }) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const canEdit = !archived && can(Permissions.propertiesUpdate)
  const canArchive = !archived && can(Permissions.propertiesArchive)
  const [showArchived, setShowArchived] = useState(false)
  const [editing, setEditing] = useState<Unit | 'new' | null>(null)
  const units = useQuery({
    queryKey: ['properties', propertyId, 'units', showArchived],
    queryFn: () => get<Unit[]>(`/api/v1/properties/${propertyId}/units${showArchived ? '?includeArchived=true' : ''}`),
  })
  const archive = useMutation({
    mutationFn: (unit: Unit) => post<Unit>(`/api/v1/units/${unit.id}/archive`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['properties'] }),
  })

  return (
    <Card
      title="Units"
      actions={
        canEdit &&
        editing === null && (
          <Button variant="secondary" onClick={() => setEditing('new')}>
            Add unit
          </Button>
        )
      }
    >
      {editing !== null && (
        <UnitForm propertyId={propertyId} unit={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />
      )}
      <ErrorBox error={units.error ?? archive.error} />
      {units.data?.length === 0 && <Empty>No units yet. Add flats, shops or rooms to let them.</Empty>}
      {!!units.data?.length && (
        <div className="relative -mx-5 overflow-x-auto">
          <table className="w-full min-w-[40rem] text-left text-sm">
            <thead className="text-xs text-slate-500 uppercase">
              <tr>
                <th className="px-5 py-2 font-medium">Unit</th>
                <th className="px-3 py-2 font-medium">Type</th>
                <th className="px-3 py-2 font-medium">Beds/baths</th>
                <th className="px-3 py-2 text-right font-medium">Rent</th>
                <th className="px-3 py-2 font-medium">Status</th>
                <th className="px-5 py-2">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-100">
              {units.data.map((u) => (
                <tr key={u.id} className={u.isArchived ? 'text-slate-400' : ''}>
                  <td className="px-5 py-2.5">
                    <span className="font-medium">{u.code}</span>
                    {u.floor && <span className="block text-xs text-slate-500">Floor {u.floor}</span>}
                  </td>
                  <td className="px-3 py-2.5">{enumLabel(u.type)}</td>
                  <td className="px-3 py-2.5 whitespace-nowrap">
                    {u.bedrooms ?? '–'} / {u.bathrooms ?? '–'}
                  </td>
                  <td className="px-3 py-2.5 text-right whitespace-nowrap tabular-nums">
                    {formatNaira(u.defaultRent)}
                    <span className="block text-xs text-slate-500">{enumLabel(u.billingFrequency)}</span>
                  </td>
                  <td className="px-3 py-2.5">
                    {u.isArchived ? <Badge>Archived</Badge> : <Badge tone={statusTone(u.occupancyStatus)}>{u.occupancyStatus}</Badge>}
                  </td>
                  <td className="px-5 py-2.5 text-right whitespace-nowrap">
                    {!u.isArchived && canEdit && (
                      <Button variant="ghost" aria-label={`Edit unit ${u.code}`} onClick={() => setEditing(u)}>
                        Edit
                      </Button>
                    )}
                    {!u.isArchived && canArchive && u.occupancyStatus !== 'Occupied' && (
                      <Button
                        variant="ghost"
                        aria-label={`Archive unit ${u.code}`}
                        onClick={() => {
                          if (confirm(`Archive unit ${u.code}? It will no longer be available to let.`)) archive.mutate(u)
                        }}
                      >
                        Archive
                      </Button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      <label className="mt-4 flex items-center gap-2 text-sm text-slate-600">
        <input type="checkbox" checked={showArchived} onChange={(e) => setShowArchived(e.target.checked)} /> Show archived units
      </label>
    </Card>
  )
}

const schema = z.object({
  code: z.string().trim().min(1, 'Enter the unit code, e.g. A1.').max(30),
  type: z.enum(UnitTypes),
  floor: optionalText(20),
  bedrooms: optionalWhole,
  bathrooms: optionalWhole,
  sizeSquareMetres: optionalAmount,
  defaultRent: requiredAmount,
  billingFrequency: z.enum(BillingFrequencies),
  serviceChargeSharePercent: optionalAmount.refine((v) => v === '' || Number(v) <= 100, 'Enter a percentage up to 100.'),
  occupancyStatus: z.enum(OccupancyStatuses),
  notes: optionalText(2000),
})
type Values = z.infer<typeof schema>

function UnitForm({ propertyId, unit, onDone }: { propertyId: string; unit: Unit | null; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState, control } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      code: unit?.code ?? '',
      type: unit?.type ?? 'Flat',
      floor: unit?.floor ?? '',
      bedrooms: unit?.bedrooms?.toString() ?? '',
      bathrooms: unit?.bathrooms?.toString() ?? '',
      sizeSquareMetres: unit?.sizeSquareMetres?.toString() ?? '',
      defaultRent: unit?.defaultRent.toString() ?? '',
      billingFrequency: unit?.billingFrequency ?? 'Annual',
      serviceChargeSharePercent: unit?.serviceChargeSharePercent?.toString() ?? '',
      occupancyStatus: unit?.occupancyStatus ?? 'Vacant',
      notes: unit?.notes ?? '',
    },
  })
  const errors = formState.errors
  const rent = useWatch({ control, name: 'defaultRent' })

  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        code: v.code.trim(),
        type: v.type,
        floor: blankToNull(v.floor),
        bedrooms: numberOrNull(v.bedrooms),
        bathrooms: numberOrNull(v.bathrooms),
        sizeSquareMetres: numberOrNull(v.sizeSquareMetres),
        defaultRent: Number(v.defaultRent),
        currency: 'NGN',
        billingFrequency: v.billingFrequency,
        serviceChargeSharePercent: numberOrNull(v.serviceChargeSharePercent),
        amenities: unit?.amenities ?? null,
        notes: blankToNull(v.notes),
        occupancyStatus: v.occupancyStatus,
      }
      return unit ? put<Unit>(`/api/v1/units/${unit.id}`, body) : post<Unit>(`/api/v1/properties/${propertyId}/units`, body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['properties'] })
      onDone()
    },
  })

  return (
    <form className="mb-6 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
      <h3 className="text-sm font-semibold">{unit ? `Edit unit ${unit.code}` : 'New unit'}</h3>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-3">
        <TextField label="Unit code" {...register('code')} error={errors.code?.message} />
        <SelectField label="Unit type" {...register('type')}>
          {UnitTypes.map((t) => (
            <option key={t} value={t}>
              {t}
            </option>
          ))}
        </SelectField>
        <TextField label="Floor" {...register('floor')} error={errors.floor?.message} />
        <TextField label="Bedrooms" inputMode="numeric" {...register('bedrooms')} error={errors.bedrooms?.message} />
        <TextField label="Bathrooms" inputMode="numeric" {...register('bathrooms')} error={errors.bathrooms?.message} />
        <TextField label="Size (m²)" inputMode="decimal" {...register('sizeSquareMetres')} error={errors.sizeSquareMetres?.message} />
        <TextField
          label="Default rent (₦)"
          inputMode="decimal"
          hint={/^\d+(\.\d{1,2})?$/.test(rent.trim()) ? formatNaira(Number(rent)) : 'Amount per billing period.'}
          {...register('defaultRent')}
          error={errors.defaultRent?.message}
        />
        <SelectField label="Billing frequency" {...register('billingFrequency')}>
          {BillingFrequencies.map((f) => (
            <option key={f} value={f}>
              {enumLabel(f)}
            </option>
          ))}
        </SelectField>
        <TextField
          label="Service charge share (%)"
          inputMode="decimal"
          {...register('serviceChargeSharePercent')}
          error={errors.serviceChargeSharePercent?.message}
        />
        <SelectField label="Occupancy" {...register('occupancyStatus')}>
          {OccupancyStatuses.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </SelectField>
      </div>
      <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save unit
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
