import { useState } from 'react'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { LeadStatus, LeadSummary, ListingStatus, ListingSummary } from '../../lib/types'
import { LeadStatuses, ListingStatuses } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader } from '../../components/ui'
import { publicCompanyPath, rentLabel } from './listingFormat'

/** Vacant units advertised to the public, and the enquiries they bring in. */
export function ListingsPage() {
  const { can, organization } = useSession()
  const navigate = useNavigate()
  const [params, setParams] = useSearchParams()
  const canLeads = can(Permissions.leadsManage)
  const tab = params.get('tab') === 'enquiries' && canLeads ? 'enquiries' : 'listings'

  const tabClass = (active: boolean) =>
    `rounded-md px-3 py-1.5 text-sm font-medium ${active ? 'bg-white text-slate-900 shadow-sm ring-1 ring-slate-200' : 'text-slate-600 hover:text-slate-900'}`

  return (
    <>
      <PageHeader
        title="Listings"
        description="Advertise vacant units, take enquiries and book viewings."
        actions={
          <div className="flex flex-wrap gap-2">
            {organization && (
              <a
                href={publicCompanyPath(organization.slug)}
                target="_blank"
                rel="noreferrer"
                className="rounded-md px-3.5 py-2 text-sm font-medium text-brand-700 ring-1 ring-slate-300 hover:bg-slate-50"
              >
                View public page ↗
              </a>
            )}
            {can(Permissions.listingsManage) && <Button onClick={() => navigate('/app/listings/new')}>New listing</Button>}
          </div>
        }
      />
      {canLeads && (
        <div role="tablist" className="mb-4 inline-flex gap-1 rounded-lg bg-slate-100 p-1">
          <button role="tab" aria-selected={tab === 'listings'} className={tabClass(tab === 'listings')} onClick={() => setParams({})}>
            Listings
          </button>
          <button
            role="tab"
            aria-selected={tab === 'enquiries'}
            className={tabClass(tab === 'enquiries')}
            onClick={() => setParams({ tab: 'enquiries' })}
          >
            Enquiries
          </button>
        </div>
      )}
      {tab === 'listings' ? (
        <ListingList />
      ) : (
        <Card>
          <LeadList />
        </Card>
      )}
    </>
  )
}

function ListingList() {
  const [status, setStatus] = useState<ListingStatus | ''>('')
  const listings = useQuery({
    queryKey: ['listings', 'list', status],
    queryFn: () => get<ListingSummary[]>(`/api/v1/listings${status ? `?status=${status}` : ''}`),
  })
  return (
    <Card>
      <label className="mb-3 flex items-center gap-2 text-sm text-slate-600">
        Show
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value as ListingStatus | '')}
          className="rounded-md border-0 py-1.5 pr-8 pl-2 text-sm ring-1 ring-slate-300"
        >
          <option value="">All listings</option>
          {ListingStatuses.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </select>
      </label>
      <ErrorBox error={listings.error} />
      {listings.data?.length === 0 && (
        <Empty>
          {status ? 'No listings with this status.' : 'No listings yet. Create one for a vacant unit to start taking enquiries.'}
        </Empty>
      )}
      <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
        {listings.data?.map((l) => (
          <li key={l.id}>
            <Link
              to={`/app/listings/${l.id}`}
              className="flex flex-wrap items-start justify-between gap-3 px-5 py-3 text-sm hover:bg-slate-50"
            >
              <div className="min-w-0">
                <p className="font-medium text-slate-900">{l.headline}</p>
                <p className="text-slate-600">
                  {l.propertyName} · unit {l.unitCode} · {rentLabel(l.askingRent, l.billingFrequency)}
                </p>
                <p className="text-xs text-slate-500">
                  {l.photoCount} {l.photoCount === 1 ? 'photo' : 'photos'}
                  {l.agentName && ` · ${l.agentName}`}
                </p>
              </div>
              <div className="flex flex-wrap items-center gap-2">
                {l.openLeads > 0 && (
                  <Badge tone="amber">
                    {l.openLeads} open {l.openLeads === 1 ? 'enquiry' : 'enquiries'}
                  </Badge>
                )}
                <Badge tone={statusTone(l.closeReason ?? l.status)}>{l.closeReason ? `Closed · ${l.closeReason}` : l.status}</Badge>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </Card>
  )
}

/** Enquiries, for every listing the member can see or for one listing. */
export function LeadList({ listingId }: { listingId?: string }) {
  const [status, setStatus] = useState<LeadStatus | ''>('')
  const [openOnly, setOpenOnly] = useState(true)
  const query = new URLSearchParams()
  if (listingId) query.set('listingId', listingId)
  if (status) query.set('status', status)
  else if (openOnly) query.set('open', 'true')
  const leads = useQuery({
    queryKey: ['leads', 'list', listingId ?? null, status, openOnly],
    queryFn: () => get<LeadSummary[]>(`/api/v1/leads?${query}`),
  })
  return (
    <>
      <div className="mb-3 flex flex-wrap items-center gap-4 text-sm text-slate-600">
        <label className="flex items-center gap-2">
          Status
          <select
            value={status}
            onChange={(e) => setStatus(e.target.value as LeadStatus | '')}
            className="rounded-md border-0 py-1.5 pr-8 pl-2 text-sm ring-1 ring-slate-300"
          >
            <option value="">Any</option>
            {LeadStatuses.map((s) => (
              <option key={s} value={s}>
                {enumLabel(s)}
              </option>
            ))}
          </select>
        </label>
        {!status && (
          <label className="flex items-center gap-2">
            <input type="checkbox" checked={openOnly} onChange={(e) => setOpenOnly(e.target.checked)} />
            Open only
          </label>
        )}
      </div>
      <ErrorBox error={leads.error} />
      {leads.data?.length === 0 && <Empty>No enquiries to show.</Empty>}
      <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
        {leads.data?.map((l) => (
          <li key={l.id}>
            <Link
              to={`/app/leads/${l.id}`}
              className="flex flex-wrap items-start justify-between gap-3 px-5 py-3 text-sm hover:bg-slate-50"
            >
              <div className="min-w-0">
                <p className="font-medium text-slate-900">
                  {l.name} <span className="font-normal text-slate-500">· {l.reference}</span>
                </p>
                <p className="text-slate-600">{[l.phone, l.email].filter(Boolean).join(' · ')}</p>
                {!listingId && (
                  <p className="text-xs text-slate-500">
                    {l.listingHeadline} · {l.propertyName}, unit {l.unitCode}
                  </p>
                )}
              </div>
              <div className="flex flex-col items-end gap-1">
                <Badge tone={statusTone(l.status)}>{enumLabel(l.status)}</Badge>
                <span className="text-xs text-slate-500">
                  {l.nextViewingAtUtc
                    ? `Viewing ${formatDate(l.nextViewingAtUtc)}`
                    : `${enumLabel(l.source)} · ${formatDate(l.createdAtUtc)}`}
                </span>
              </div>
            </Link>
          </li>
        ))}
      </ul>
    </>
  )
}
