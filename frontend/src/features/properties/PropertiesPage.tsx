import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Paged, PropertySummary } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { enumLabel } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, Pager, SearchInput } from '../../components/ui'
import { PropertyForm } from './PropertyForm'

const PAGE_SIZE = 25

export function PropertiesPage() {
  const { can } = useSession()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [includeArchived, setIncludeArchived] = useState(false)
  const [page, setPage] = useState(1)
  const [adding, setAdding] = useState(false)
  const term = useDebounced(search.trim())

  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (term) params.set('search', term)
  if (includeArchived) params.set('includeArchived', 'true')
  const properties = useQuery({
    queryKey: ['properties', 'list', term, includeArchived, page],
    queryFn: () => get<Paged<PropertySummary>>(`/api/v1/properties?${params}`),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader
        title="Properties"
        description="Buildings and estates you manage, with their units and owners."
        actions={can(Permissions.propertiesCreate) && !adding && <Button onClick={() => setAdding(true)}>Add property</Button>}
      />
      {adding && <PropertyForm property={null} onDone={(saved) => (saved ? navigate(`/app/properties/${saved.id}`) : setAdding(false))} />}
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-x-4">
          <div className="min-w-0 flex-1">
            <SearchInput
              label="Search code, name or address"
              value={search}
              onChange={(v) => {
                setSearch(v)
                setPage(1)
              }}
            />
          </div>
          <label className="mb-4 flex items-center gap-2 py-2 text-sm text-slate-700">
            <input
              type="checkbox"
              checked={includeArchived}
              onChange={(e) => {
                setIncludeArchived(e.target.checked)
                setPage(1)
              }}
            />
            Show archived
          </label>
        </div>
        <ErrorBox error={properties.error} />
        {properties.data?.items.length === 0 && <Empty>{term ? 'No properties match your search.' : 'No properties yet.'}</Empty>}
        {!!properties.data?.items.length && (
          <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
            {properties.data.items.map((p) => (
              <li key={p.id}>
                <Link to={`/app/properties/${p.id}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 hover:bg-slate-50">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-slate-900">
                      <span className="mr-2 font-mono text-xs text-slate-500">{p.code}</span>
                      {p.name}
                    </p>
                    <p className="truncate text-sm text-slate-600">
                      {p.addressLine}
                      {p.city ? `, ${p.city}` : ''}, {p.state}
                    </p>
                    {p.branchName && <p className="text-xs text-slate-500">{p.branchName} branch</p>}
                  </div>
                  <div className="flex items-center gap-3 text-sm">
                    <span className="text-slate-600">
                      {p.occupiedUnitCount}/{p.unitCount} occupied
                    </span>
                    <Badge tone={statusTone(p.status)}>{enumLabel(p.status)}</Badge>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
        {properties.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={properties.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}
