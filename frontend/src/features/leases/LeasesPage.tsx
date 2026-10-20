import { useState } from 'react'
import { Link, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { LeaseSummary, Paged } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { Card, Empty, ErrorBox, PageHeader, Pager, SearchInput } from '../../components/ui'
import { LeaseList } from './LeaseList'

const PAGE_SIZE = 25

const filters = [
  { value: '', label: 'All' },
  { value: 'ending', label: 'Ending in 90 days' },
  { value: 'Active', label: 'Active' },
  { value: 'Upcoming', label: 'Upcoming' },
  { value: 'HoldingOver', label: 'Holding over' },
  { value: 'Draft', label: 'Drafts' },
  { value: 'Ended', label: 'Ended' },
  { value: 'Terminated', label: 'Terminated' },
] as const

export function LeasesPage() {
  const { can } = useSession()
  const [params, setParams] = useSearchParams()
  const filter = params.get('filter') ?? ''
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const term = useDebounced(search.trim())

  const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (term) query.set('search', term)
  if (filter === 'ending') query.set('endingWithinDays', '90')
  else if (filter) query.set('stage', filter)
  const leases = useQuery({
    queryKey: ['leases', 'list', term, filter, page],
    queryFn: () => get<Paged<LeaseSummary>>(`/api/v1/leases?${query}`),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader
        title="Leases"
        description="Who rents which unit, on what terms, and when each lease ends."
        actions={
          can(Permissions.leasesCreate) && (
            <Link to="/app/leases/new" className="rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700">
              New lease
            </Link>
          )
        }
      />
      <Card>
        <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Filter leases">
          {filters.map((f) => (
            <button
              key={f.value}
              aria-pressed={filter === f.value}
              onClick={() => {
                setParams(f.value ? { filter: f.value } : {})
                setPage(1)
              }}
              className={`rounded-full px-3 py-1 text-sm font-medium ring-1 ${
                filter === f.value ? 'bg-brand-50 text-brand-700 ring-brand-200' : 'text-slate-600 ring-slate-200 hover:bg-slate-50'
              }`}
            >
              {f.label}
            </button>
          ))}
        </div>
        <SearchInput
          label="Search by tenant, unit, property or lease number"
          value={search}
          onChange={(v) => {
            setSearch(v)
            setPage(1)
          }}
        />
        <ErrorBox error={leases.error} />
        {leases.data?.items.length === 0 && <Empty>{term || filter ? 'No leases match.' : 'No leases yet.'}</Empty>}
        {!!leases.data?.items.length && <LeaseList leases={leases.data.items} />}
        {leases.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={leases.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}

/** Leases for one property or tenant, for their detail pages. */
export function LeasesPanel({ filter, newLeaseQuery, showProperty = true }: { filter: string; newLeaseQuery?: string; showProperty?: boolean }) {
  const { can } = useSession()
  const leases = useQuery({
    queryKey: ['leases', 'panel', filter],
    queryFn: () => get<Paged<LeaseSummary>>(`/api/v1/leases?${filter}&pageSize=50`),
  })
  const visible = leases.data?.items.filter((l) => l.status !== 'Cancelled') ?? []
  return (
    <Card
      title="Leases"
      actions={
        can(Permissions.leasesCreate) &&
        newLeaseQuery !== undefined && (
          <Link to={`/app/leases/new?${newLeaseQuery}`} className="text-sm font-medium text-brand-700 hover:underline">
            New lease
          </Link>
        )
      }
    >
      <ErrorBox error={leases.error} />
      {leases.data && visible.length === 0 && <Empty>No leases yet.</Empty>}
      {visible.length > 0 && <LeaseList leases={visible} showProperty={showProperty} />}
    </Card>
  )
}
