import { useState } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Paged, TenantSummary } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, Pager, SearchInput } from '../../components/ui'
import { TenantForm } from './TenantForm'

const PAGE_SIZE = 25

export function TenantsPage() {
  const { can } = useSession()
  const navigate = useNavigate()
  const [search, setSearch] = useState('')
  const [page, setPage] = useState(1)
  const [adding, setAdding] = useState(false)
  const term = useDebounced(search.trim())
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE) })
  if (term) params.set('search', term)
  const tenants = useQuery({
    queryKey: ['tenants', 'list', term, page],
    queryFn: () => get<Paged<TenantSummary>>(`/api/v1/tenants?${params}`),
    placeholderData: keepPreviousData,
  })

  return (
    <>
      <PageHeader
        title="Tenants"
        description="People and companies renting from you, with their identity checks and guarantors."
        actions={can(Permissions.tenantsCreate) && !adding && <Button onClick={() => setAdding(true)}>Add tenant</Button>}
      />
      {adding && <TenantForm tenant={null} onDone={(saved) => (saved ? navigate(`/app/tenants/${saved.id}`) : setAdding(false))} />}
      <Card>
        <SearchInput
          label="Search by name, email or phone"
          value={search}
          onChange={(v) => {
            setSearch(v)
            setPage(1)
          }}
        />
        <ErrorBox error={tenants.error} />
        {tenants.data?.items.length === 0 && <Empty>{term ? 'No tenants match your search.' : 'No tenants yet.'}</Empty>}
        {!!tenants.data?.items.length && (
          <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
            {tenants.data.items.map((t) => (
              <li key={t.id}>
                <Link to={`/app/tenants/${t.id}`} className="flex flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 hover:bg-slate-50">
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-slate-900">
                      {t.name} {t.type === 'Company' && <span className="text-xs font-normal text-slate-500">(company)</span>}
                    </p>
                    <p className="truncate text-sm text-slate-600">{[t.email, t.phone, t.employer].filter(Boolean).join(' · ')}</p>
                  </div>
                  <div className="flex flex-wrap items-center gap-2 text-sm">
                    <span className="text-slate-600">
                      {t.guarantorCount} {t.guarantorCount === 1 ? 'guarantor' : 'guarantors'}
                    </span>
                    <Badge tone={statusTone(t.kycStatus)}>KYC {t.kycStatus.toLowerCase()}</Badge>
                  </div>
                </Link>
              </li>
            ))}
          </ul>
        )}
        {tenants.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={tenants.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}
