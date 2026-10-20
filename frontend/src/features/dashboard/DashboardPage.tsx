import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Branch, Member, Paged } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions, roleLabel } from '../../lib/permissions'
import { Card, PageHeader } from '../../components/ui'
import { ExpiringDocuments } from '../documents/ExpiringDocuments'

const comingNext = [
  { phase: 'Phase 4', items: 'Rent invoices, payments, receipts and bank matching, once the finance design is signed off' },
  { phase: 'Phase 5', items: 'Owner statements, payouts and expenses (maintenance is live)' },
  { phase: 'Phase 6', items: 'Service charges, listings and reports (owner and tenant portals and documents are live)' },
]

export function DashboardPage() {
  const { me, organization, can } = useSession()
  const branches = useQuery({
    queryKey: ['branches'],
    queryFn: () => get<Branch[]>('/api/v1/branches'),
    enabled: can(Permissions.branchesView),
  })
  const members = useQuery({
    queryKey: ['members', organization?.id],
    queryFn: () => get<Member[]>(`/api/v1/organizations/${organization!.id}/members`),
    enabled: can(Permissions.usersView),
  })

  return (
    <>
      <PageHeader
        title={`Welcome, ${me?.fullName.split(' ')[0]}`}
        description={`${organization?.name} · ${organization?.roles.map((r) => roleLabel(r, organization.roleNames)).join(', ')}`}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        {can(Permissions.propertiesView) && (
          <CountCard title="Properties" to="/app/properties" path="/api/v1/properties" text="Buildings and estates you manage." />
        )}
        {can(Permissions.ownersView) && (
          <CountCard title="Owners" to="/app/owners" path="/api/v1/owners" text="Landlords you manage property for." />
        )}
        {can(Permissions.tenantsView) && (
          <CountCard title="Tenants" to="/app/tenants" path="/api/v1/tenants" text="People and companies renting from you." />
        )}
        {can(Permissions.leasesView) && (
          <CountCard title="Leases" to="/app/leases" path="/api/v1/leases" text="Every lease, from draft to ended." />
        )}
        {can(Permissions.leasesView) && (
          <CountCard
            title="Ending in 90 days"
            to="/app/leases?filter=ending"
            path="/api/v1/leases?endingWithinDays=90"
            text="Active leases to renew or wind down, including any past their end date."
          />
        )}
        {can(Permissions.maintenanceView) && (
          <CountCard
            title="Open maintenance"
            to="/app/maintenance"
            path="/api/v1/maintenance?open=true"
            text="Repairs not yet completed, rejected or cancelled."
          />
        )}
        {!can(Permissions.maintenanceView) && can(Permissions.maintenanceUpdateAssigned) && (
          <CountCard
            title="My jobs"
            to="/app/maintenance?filter=mine"
            path="/api/v1/maintenance?open=true&mine=true"
            text="Open repair jobs assigned to you."
          />
        )}
        {can(Permissions.branchesView) && (
          <Card
            title="Branches"
            actions={
              <Link to="/app/branches" className="text-sm font-medium text-brand-700 hover:underline">
                Manage
              </Link>
            }
          >
            <p className="text-3xl font-semibold">{branches.data?.length ?? '–'}</p>
            <p className="mt-1 text-sm text-slate-600">Offices you can limit staff access to.</p>
          </Card>
        )}
        {can(Permissions.usersView) && (
          <Card
            title="Team"
            actions={
              <Link to="/app/team" className="text-sm font-medium text-brand-700 hover:underline">
                Manage
              </Link>
            }
          >
            <p className="text-3xl font-semibold">{members.data?.length ?? '–'}</p>
            <p className="mt-1 text-sm text-slate-600">Active members of {organization?.name}.</p>
          </Card>
        )}
      </div>
      {can(Permissions.documentsView) && (
        <div className="mt-6">
          <ExpiringDocuments />
        </div>
      )}
      <div className="mt-6">
        <Card title="Coming next">
          <ul className="divide-y divide-slate-100">
            {comingNext.map((c) => (
              <li key={c.phase} className="flex gap-4 py-2.5 text-sm">
                <span className="w-16 shrink-0 font-medium text-slate-500">{c.phase}</span>
                <span className="text-slate-700">{c.items}</span>
              </li>
            ))}
          </ul>
        </Card>
      </div>
    </>
  )
}

/** A total from a paged list, read with a one-item page. */
function CountCard({ title, to, path, text }: { title: string; to: string; path: string; text: string }) {
  const count = useQuery({
    queryKey: [title.toLowerCase(), 'count'],
    queryFn: () => get<Paged<unknown>>(`${path}${path.includes('?') ? '&' : '?'}pageSize=1`),
  })
  return (
    <Card
      title={title}
      actions={
        <Link to={to} className="text-sm font-medium text-brand-700 hover:underline">
          View
        </Link>
      }
    >
      <p className="text-3xl font-semibold">{count.data?.totalCount ?? '–'}</p>
      <p className="mt-1 text-sm text-slate-600">{text}</p>
    </Card>
  )
}
