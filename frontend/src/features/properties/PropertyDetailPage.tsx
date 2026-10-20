import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { PropertyDetail } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, DetailList, ErrorBox, PageHeader } from '../../components/ui'
import { PropertyForm } from './PropertyForm'
import { UnitsPanel } from './UnitsPanel'
import { OwnershipPanel } from './OwnershipPanel'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { LeasesPanel } from '../leases/LeasesPage'
import { MaintenancePanel } from '../maintenance/MaintenancePage'

export function PropertyDetailPage() {
  const { id = '' } = useParams()
  const { can } = useSession()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const property = useQuery({ queryKey: ['properties', id], queryFn: () => get<PropertyDetail>(`/api/v1/properties/${id}`) })
  const archive = useMutation({
    mutationFn: () => post<PropertyDetail>(`/api/v1/properties/${id}/archive`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['properties'] }),
  })

  const p = property.data
  if (!p) {
    return (
      <>
        <BackLink />
        <ErrorBox error={property.error} />
        {property.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }
  const archived = p.status === 'Archived'

  return (
    <>
      <BackLink />
      <PageHeader
        title={p.name}
        description={`${p.code} · ${p.addressLine}${p.city ? `, ${p.city}` : ''}, ${p.state}`}
        actions={
          !editing &&
          !archived && (
            <div className="flex gap-2">
              {can(Permissions.propertiesUpdate) && (
                <Button variant="secondary" onClick={() => setEditing(true)}>
                  Edit
                </Button>
              )}
              {can(Permissions.propertiesArchive) && (
                <Button
                  variant="danger"
                  disabled={archive.isPending}
                  onClick={() => {
                    if (confirm(`Archive ${p.name}? It will be hidden from lists and can no longer be changed.`)) archive.mutate()
                  }}
                >
                  Archive
                </Button>
              )}
            </div>
          )
        }
      />
      {editing && <PropertyForm property={p} onDone={() => setEditing(false)} />}
      <div className="space-y-6">
        <ErrorBox error={archive.error} />
        <Card>
          <div className="mb-4 flex flex-wrap items-center gap-2">
            <Badge tone={statusTone(p.status)}>{enumLabel(p.status)}</Badge>
            <span className="text-sm text-slate-600">
              {p.occupiedUnitCount} of {p.unitCount} units occupied
            </span>
          </div>
          <DetailList
            items={[
              { label: 'Type', value: p.type },
              { label: 'Branch', value: p.branchName },
              { label: 'Property manager', value: p.managerName },
              { label: 'LGA', value: p.localGovernmentArea },
              { label: 'Amenities', value: p.amenities },
              { label: 'Added', value: formatDate(p.createdAtUtc) },
              ...(p.description ? [{ label: 'Description', value: p.description }] : []),
              ...(p.archivedAtUtc ? [{ label: 'Archived', value: formatDate(p.archivedAtUtc) }] : []),
            ]}
          />
        </Card>
        <UnitsPanel propertyId={p.id} archived={archived} />
        {can(Permissions.leasesView) && (
          <LeasesPanel filter={`propertyId=${p.id}`} newLeaseQuery={archived ? undefined : `propertyId=${p.id}`} showProperty={false} />
        )}
        {can(Permissions.maintenanceView) && <MaintenancePanel propertyId={p.id} archived={archived} />}
        <OwnershipPanel propertyId={p.id} archived={archived} />
        {can(Permissions.documentsView) && (
          <DocumentsPanel subject="Property" subjectId={p.id} title="Documents and photos" readOnly={archived} />
        )}
      </div>
    </>
  )
}

function BackLink() {
  return (
    <Link to="/app/properties" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
      ← Properties
    </Link>
  )
}
