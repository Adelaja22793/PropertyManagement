import { useForm, useWatch } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { LeaseSummary, MaintenanceDetail, Paged, PropertySummary, Unit } from '../../lib/types'
import { MaintenanceCategories, MaintenanceUrgencies } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel } from '../../lib/format'
import { blankToNull, optionalText } from '../../lib/validation'
import { Button, Card, ErrorBox, SelectField, TextAreaField, TextField } from '../../components/ui'

const schema = z.object({
  propertyId: z.string().min(1, 'Choose the property.'),
  unitId: z.string(),
  linkTenant: z.boolean(),
  reportedBy: optionalText(200),
  title: z.string().trim().min(3, 'Describe the problem in a few words.').max(200),
  description: optionalText(4000),
  category: z.enum(MaintenanceCategories),
  urgency: z.enum(MaintenanceUrgencies),
})
type Values = z.infer<typeof schema>

const urgencyHints: Record<string, string> = {
  Emergency: 'Danger to people or the building, such as flooding, fire risk or no security. Act today.',
  High: 'Stops the tenant using part of the home, such as no water or no power.',
  Normal: 'Needs fixing, but the home is usable.',
  Low: 'Cosmetic or can wait for the next visit.',
}

export function MaintenanceForm(props: {
  request: MaintenanceDetail | null
  presetPropertyId?: string | null
  onDone: (saved?: MaintenanceDetail) => void
}) {
  // The property list loads first, so a preset property is selected once its option exists.
  const properties = useQuery({
    queryKey: ['properties', 'picker'],
    queryFn: () => get<Paged<PropertySummary>>('/api/v1/properties?pageSize=100'),
  })
  if (!properties.data) {
    return (
      <div className="mb-6">
        <Card title={props.request ? `Edit ${props.request.reference}` : 'Report a repair'}>
          <ErrorBox error={properties.error} />
          {properties.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
        </Card>
      </div>
    )
  }
  return <RequestForm {...props} properties={properties.data.items} />
}

function RequestForm({
  request,
  presetPropertyId,
  onDone,
  properties,
}: {
  request: MaintenanceDetail | null
  presetPropertyId?: string | null
  onDone: (saved?: MaintenanceDetail) => void
  properties: PropertySummary[]
}) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState, control, setValue } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      propertyId: request?.propertyId ?? presetPropertyId ?? '',
      unitId: request?.unitId ?? '',
      linkTenant: request ? request.tenantId !== null : true,
      reportedBy: request?.reportedBy ?? '',
      title: request?.title ?? '',
      description: request?.description ?? '',
      category: request?.category ?? 'Plumbing',
      urgency: request?.urgency ?? 'Normal',
    },
  })
  const [propertyId, unitId, urgency, linkTenant] = useWatch({ control, name: ['propertyId', 'unitId', 'urgency', 'linkTenant'] })
  const units = useQuery({
    queryKey: ['properties', propertyId, 'units'],
    queryFn: () => get<Unit[]>(`/api/v1/properties/${propertyId}/units`),
    enabled: !!propertyId,
  })
  // The unit's current tenant, so the request is linked to them.
  const lease = useQuery({
    queryKey: ['leases', 'unit-current', unitId],
    queryFn: () => get<Paged<LeaseSummary>>(`/api/v1/leases?unitId=${unitId}&stage=Active&pageSize=1`),
    enabled: !!unitId && can(Permissions.leasesView),
  })
  const tenant = request && request.unitId === unitId && request.tenantId
    ? { id: request.tenantId, name: request.tenantName }
    : lease.data?.items[0]?.primaryTenantId
      ? { id: lease.data.items[0].primaryTenantId, name: lease.data.items[0].primaryTenantName }
      : null

  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        propertyId: v.propertyId,
        unitId: v.unitId || null,
        tenantId: v.unitId && v.linkTenant && tenant ? tenant.id : null,
        reportedBy: blankToNull(v.reportedBy),
        title: v.title.trim(),
        description: blankToNull(v.description),
        category: v.category,
        urgency: v.urgency,
      }
      return request ? put<MaintenanceDetail>(`/api/v1/maintenance/${request.id}`, body) : post<MaintenanceDetail>('/api/v1/maintenance', body)
    },
    onSuccess: async (saved) => {
      await queryClient.invalidateQueries({ queryKey: ['maintenance'] })
      onDone(saved)
    },
  })
  const errors = formState.errors
  const activeProperties = properties.filter((p) => p.status !== 'Archived' || p.id === request?.propertyId)

  return (
    <div className="mb-6">
      <Card title={request ? `Edit ${request.reference}` : 'Report a repair'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <SelectField
              label="Property"
              disabled={!!request}
              {...register('propertyId', { onChange: () => setValue('unitId', '') })}
              error={errors.propertyId?.message}
            >
              <option value="">Choose a property</option>
              {activeProperties.map((p) => (
                <option key={p.id} value={p.id}>
                  {p.code} · {p.name}
                </option>
              ))}
            </SelectField>
            <SelectField label="Unit" disabled={!propertyId} {...register('unitId')}>
              <option value="">Common area or whole property</option>
              {units.data?.map((u) => (
                <option key={u.id} value={u.id}>
                  {u.code}
                </option>
              ))}
            </SelectField>
            <TextField label="Problem" placeholder="Kitchen sink leaking" {...register('title')} error={errors.title?.message} />
            <TextField label="Reported by" placeholder="Tenant by phone, caretaker…" {...register('reportedBy')} error={errors.reportedBy?.message} />
            <SelectField label="Category" {...register('category')}>
              {MaintenanceCategories.map((c) => (
                <option key={c} value={c}>
                  {enumLabel(c)}
                </option>
              ))}
            </SelectField>
            <SelectField label="Urgency" hint={urgencyHints[urgency]} {...register('urgency')}>
              {MaintenanceUrgencies.map((u) => (
                <option key={u} value={u}>
                  {u}
                </option>
              ))}
            </SelectField>
          </div>
          {unitId && tenant && (
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" {...register('linkTenant')} /> Link to the tenant, {tenant.name}
            </label>
          )}
          {unitId && tenant && !linkTenant && <p className="text-xs text-slate-500">The request won't show on the tenant's record.</p>}
          <TextAreaField label="Details" rows={3} {...register('description')} error={errors.description?.message} />
          <p className="text-xs text-slate-500">Photo uploads arrive with document storage.</p>
          <div className="flex gap-2">
            <Button type="submit" disabled={save.isPending}>
              {request ? 'Save changes' : 'Save request'}
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
