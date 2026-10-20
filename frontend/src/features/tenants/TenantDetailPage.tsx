import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { TenantDetail } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { Badge, Button, Card, DetailList, ErrorBox, PageHeader } from '../../components/ui'
import { PortalInvite } from '../../components/PortalInvite'
import { TenantForm } from './TenantForm'
import { KycPanel } from './KycPanel'
import { GuarantorsPanel } from './GuarantorsPanel'
import { ScreeningPanel } from './ScreeningPanel'
import { DocumentsPanel } from '../documents/DocumentsPanel'
import { ExportButton } from '../exports/ExportButton'
import { LeasesPanel } from '../leases/LeasesPage'

export function TenantDetailPage() {
  const { id = '' } = useParams()
  const { can } = useSession()
  const canUpdate = can(Permissions.tenantsUpdate)
  const [editing, setEditing] = useState(false)
  const tenant = useQuery({ queryKey: ['tenants', id], queryFn: () => get<TenantDetail>(`/api/v1/tenants/${id}`) })
  const t = tenant.data

  const back = (
    <Link to="/app/tenants" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
      ← Tenants
    </Link>
  )
  if (!t) {
    return (
      <>
        {back}
        <ErrorBox error={tenant.error} />
        {tenant.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }
  const company = t.type === 'Company'

  return (
    <>
      {back}
      <PageHeader
        title={t.name}
        description={company ? 'Company tenant' : 'Individual tenant'}
        actions={
          <div className="flex flex-wrap items-start gap-2">
            {can(Permissions.dataExport) && (
              <ExportButton path={`/api/v1/exports/tenants/${t.id}`} fallbackName="tenant-data.zip" label="Download their data" />
            )}
            {canUpdate && !editing && (
              <Button variant="secondary" onClick={() => setEditing(true)}>
                Edit
              </Button>
            )}
          </div>
        }
      />
      {editing && <TenantForm tenant={t} onDone={() => setEditing(false)} />}
      <div className="space-y-6">
        <Card>
          <DetailList
            items={[
              { label: 'Email', value: t.email },
              { label: 'Phone', value: t.phone },
              { label: 'Alternate phone', value: t.alternatePhone },
              { label: 'Address', value: t.address },
              ...(company
                ? [
                    { label: 'RC number', value: t.companyRegistrationNumber },
                    { label: 'TIN', value: t.taxIdentificationNumber },
                  ]
                : [
                    { label: 'Employer', value: t.employer },
                    { label: 'Occupation', value: t.occupation },
                  ]),
              {
                label: 'Emergency contact',
                value: [t.emergencyContactName, t.emergencyContactPhone].filter(Boolean).join(' · '),
              },
              { label: 'Portal', value: t.hasPortalAccess ? <Badge tone="green">Has access</Badge> : 'Not invited' },
              ...(t.notes ? [{ label: 'Notes', value: t.notes }] : []),
            ]}
          />
        </Card>
        {can(Permissions.leasesView) && <LeasesPanel filter={`tenantId=${t.id}`} newLeaseQuery={`tenantId=${t.id}`} />}
        <KycPanel tenantId={t.id} kyc={t.kyc} />
        <GuarantorsPanel tenantId={t.id} />
        <ScreeningPanel tenantId={t.id} />
        {can(Permissions.documentsView) && <DocumentsPanel subject="Tenant" subjectId={t.id} />}
        {canUpdate && !t.hasPortalAccess && (
          <Card title="Tenant portal">
            <PortalInvite path={`/api/v1/tenants/${t.id}/portal-invite`} invalidate={['tenants', t.id]} name={t.name} />
          </Card>
        )}
      </div>
    </>
  )
}
