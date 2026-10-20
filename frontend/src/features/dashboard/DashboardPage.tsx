import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Branch, Member } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions, roleLabel } from '../../lib/permissions'
import { Card, PageHeader } from '../../components/ui'

const comingNext = [
  { phase: 'Phase 3', items: 'Properties, units, owners, tenants and guarantors' },
  { phase: 'Phase 4', items: 'Leases, rent invoices, payments, receipts and bank matching' },
  { phase: 'Phase 5', items: 'Owner statements, payouts, expenses and maintenance' },
  { phase: 'Phase 6', items: 'Owner and tenant portals, service charges and reports' },
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
        description={`${organization?.name} · ${organization?.roles.map(roleLabel).join(', ')}`}
      />
      <div className="grid gap-4 sm:grid-cols-2">
        {can(Permissions.branchesView) && (
          <Card title="Branches" actions={<Link to="/app/branches" className="text-sm font-medium text-brand-700 hover:underline">Manage</Link>}>
            <p className="text-3xl font-semibold">{branches.data?.length ?? '–'}</p>
            <p className="mt-1 text-sm text-slate-600">Offices you can limit staff access to.</p>
          </Card>
        )}
        {can(Permissions.usersView) && (
          <Card title="Team" actions={<Link to="/app/team" className="text-sm font-medium text-brand-700 hover:underline">Manage</Link>}>
            <p className="text-3xl font-semibold">{members.data?.length ?? '–'}</p>
            <p className="mt-1 text-sm text-slate-600">Active members of {organization?.name}.</p>
          </Card>
        )}
      </div>
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
