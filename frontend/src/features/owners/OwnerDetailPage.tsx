import { useState } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { OwnerDetail } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDay } from '../../lib/format'
import { Badge, Button, Card, DetailList, Empty, ErrorBox, PageHeader } from '../../components/ui'
import { PortalInvite } from '../../components/PortalInvite'
import { OwnerForm } from './OwnerForm'
import { BankAccountsPanel } from './BankAccountsPanel'
import { AgreementsPanel } from './AgreementsPanel'

export function OwnerDetailPage() {
  const { id = '' } = useParams()
  const { can } = useSession()
  const canManage = can(Permissions.ownersManage)
  const [editing, setEditing] = useState(false)
  const owner = useQuery({ queryKey: ['owners', id], queryFn: () => get<OwnerDetail>(`/api/v1/owners/${id}`) })
  const o = owner.data

  const back = (
    <Link to="/app/owners" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
      ← Owners
    </Link>
  )
  if (!o) {
    return (
      <>
        {back}
        <ErrorBox error={owner.error} />
        {owner.isLoading && <p className="text-sm text-slate-500">Loading…</p>}
      </>
    )
  }

  return (
    <>
      {back}
      <PageHeader
        title={o.name}
        description={`${o.type} owner`}
        actions={
          canManage &&
          !editing && (
            <Button variant="secondary" onClick={() => setEditing(true)}>
              Edit
            </Button>
          )
        }
      />
      {editing && <OwnerForm owner={o} onDone={() => setEditing(false)} />}
      <div className="space-y-6">
        <Card>
          <DetailList
            items={[
              { label: 'Email', value: o.email },
              { label: 'Phone', value: o.phone },
              { label: 'Address', value: o.address },
              { label: 'TIN', value: o.taxIdentificationNumber },
              { label: 'Preferred channel', value: enumLabel(o.preferredChannel) },
              { label: 'Portal', value: o.hasPortalAccess ? <Badge tone="green">Has access</Badge> : 'Not invited' },
              ...(o.notes ? [{ label: 'Notes', value: o.notes }] : []),
            ]}
          />
        </Card>

        <Card title="Properties owned">
          {o.properties.length === 0 && <Empty>Not an owner of any property yet. Set owners from a property’s page.</Empty>}
          <ul className="divide-y divide-slate-100">
            {o.properties.map((p) => (
              <li key={p.propertyId} className="flex items-center justify-between gap-4 py-2.5 text-sm">
                <div className="min-w-0">
                  {can(Permissions.propertiesView) ? (
                    <Link to={`/app/properties/${p.propertyId}`} className="font-medium text-brand-700 hover:underline">
                      {p.name}
                    </Link>
                  ) : (
                    <span className="font-medium">{p.name}</span>
                  )}
                  <p className="text-xs text-slate-500">
                    {p.code} · since {formatDay(p.effectiveFrom)}
                  </p>
                </div>
                <span className="font-semibold tabular-nums">{p.sharePercent}%</span>
              </li>
            ))}
          </ul>
        </Card>

        <BankAccountsPanel ownerId={o.id} />
        <AgreementsPanel ownerId={o.id} properties={o.properties} />

        {canManage && !o.hasPortalAccess && (
          <Card title="Owner portal">
            <PortalInvite path={`/api/v1/owners/${o.id}/portal-invite`} invalidate={['owners', o.id]} name={o.name} />
          </Card>
        )}
      </div>
    </>
  )
}
