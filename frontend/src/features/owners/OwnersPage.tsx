import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { OwnerSummary, Paged } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, Pager, SearchInput } from '../../components/ui'
import { OwnerForm } from './OwnerForm'

const PAGE_SIZE = 25

export function OwnersPage() {
  const { can } = useSession()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [adding, setAdding] = useState(false)
  const term = useDebounced(search.trim())
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (term) params.set('search', term)
  const owners = useQuery({
    queryKey: ['owners', 'list', term, page],
    queryFn: () => get<Paged<OwnerSummary>>(`/api/v1/owners?${params}`),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader
        title="Owners"
        description="Landlords whose properties you manage, their payout accounts and agreements."
        actions={can(Permissions.ownersManage) && !adding && <Button onClick={() => setAdding(true)}>Add owner</Button>}
      />
      {adding && <OwnerForm owner={null} onDone={(saved) => (saved ? navigate(`/app/owners/${saved.id}`) : setAdding(false))} />}
      <Card>
        <SearchInput
          label="Search by name, email or phone"
          value={search}
          onChange={(v) => {
            setSearch(v)
            setPage(1)
          }}
        />
        <ErrorBox error={owners.error} />
        {owners.data?.items.length === 0 && <Empty>{term ? 'No owners match your search.' : 'No owners yet.'}</Empty>}
        {!!owners.data?.items.length && (
          <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
            {owners.data.items.map((o) => (
              <li key={o.id}>
                <Link to={`/app/owners/${o.id}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 hover:bg-slate-50">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-slate-900">
                      {o.name} {o.type === 'Company' && <span className="text-xs font-normal text-slate-500">(company)</span>}
                    </p>
                    <p className="truncate text-sm text-slate-600">{[o.email, o.phone].filter(Boolean).join(' · ')}</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2 text-sm">
                    <span className="text-slate-600">
                      {o.propertyCount} {o.propertyCount === 1 ? 'property' : 'properties'}
                    </span>
                    {o.hasPendingBankAccount && <Badge tone="amber">Account pending</Badge>}
                    {!o.hasApprovedBankAccount && !o.hasPendingBankAccount && <Badge>No payout account</Badge>}
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
        {owners.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={owners.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}
