import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { MaintenanceSummary, Paged } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { Button, Card, Empty, ErrorBox, PageHeader, Pager, SearchInput } from '../../components/ui'
import { MaintenanceList } from './MaintenanceList'
import { MaintenanceForm } from './MaintenanceForm'

const PAGE_SIZE = 25

const filters = [
  { value: '', label: 'Open' },
  { value: 'emergency', label: 'Emergencies' },
  { value: 'mine', label: 'Assigned to me' },
  { value: 'Submitted', label: 'New' },
  { value: 'AwaitingParts', label: 'Awaiting parts' },
  { value: 'Completed', label: 'Completed' },
  { value: 'all', label: 'All' },
] as const

export function MaintenancePage() {
  const { can } = useSession()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const filter = params.get('filter') ?? ''
  const presetProperty = params.get('propertyId')
  const [adding, setAdding] = useState(!!presetProperty)
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const term = useDebounced(search.trim())
  const canManage = can(Permissions.maintenanceManage)

  const query = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (term) query.set('search', term)
  if (filter === '') query.set('open', 'true')
  else if (filter === 'emergency') {
    query.set('open', 'true')
    query.set('urgency', 'Emergency')
  } else if (filter === 'mine') {
    query.set('open', 'true')
    query.set('mine', 'true')
  } else if (filter !== 'all') query.set('status', filter)
  const requests = useQuery({
    queryKey: ['maintenance', 'list', term, filter, page],
    queryFn: () => get<Paged<MaintenanceSummary>>(`/api/v1/maintenance?${query}`),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader
        title="Maintenance"
        description="Repairs reported on your properties, from first report to a finished job."
        actions={
          <div className="flex flex-wrap gap-2">
            {(can(Permissions.maintenanceView) || canManage) && (
              <Link to="/app/vendors" className="rounded-md px-3.5 py-2 text-sm font-medium text-slate-700 ring-1 ring-slate-300 hover:bg-slate-50">
                Vendors
              </Link>
            )}
            {canManage && !adding && <Button onClick={() => setAdding(true)}>Report a repair</Button>}
          </div>
        }
      />
      {adding && (
        <MaintenanceForm
          request={null}
          presetPropertyId={presetProperty}
          onDone={(saved) => (saved ? navigate(`/app/maintenance/${saved.id}`) : setAdding(false))}
        />
      )}
      <Card>
        <div className="mb-4 flex flex-wrap gap-2" role="group" aria-label="Filter requests">
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
          label="Search by title or request number"
          value={search}
          onChange={(v) => {
            setSearch(v)
            setPage(1)
          }}
        />
        <ErrorBox error={requests.error} />
        {requests.data?.items.length === 0 && <Empty>{term ? 'No requests match.' : 'Nothing here.'}</Empty>}
        {!!requests.data?.items.length && <MaintenanceList items={requests.data.items} />}
        {requests.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={requests.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}

/** Open requests on one property, for its detail page. */
export function MaintenancePanel({ propertyId, archived }: { propertyId: string; archived: boolean }) {
  const { can } = useSession()
  const requests = useQuery({
    queryKey: ['maintenance', 'panel', propertyId],
    queryFn: () => get<Paged<MaintenanceSummary>>(`/api/v1/maintenance?propertyId=${propertyId}&open=true&pageSize=50`),
  })
  return (
    <Card
      title="Open maintenance"
      actions={
        can(Permissions.maintenanceManage) &&
        !archived && (
          <Link to={`/app/maintenance?propertyId=${propertyId}`} className="text-sm font-medium text-brand-700 hover:underline">
            Report a repair
          </Link>
        )
      }
    >
      <ErrorBox error={requests.error} />
      {requests.data?.items.length === 0 && <Empty>No open requests.</Empty>}
      {!!requests.data?.items.length && <MaintenanceList items={requests.data.items} showProperty={false} />}
    </Card>
  )
}
