import { Link } from 'react-router-dom'
import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { DocumentSubject, ExpiringDocument } from '../../lib/types'
import { enumLabel } from '../../lib/format'
import { Card, ErrorBox } from '../../components/ui'
import { ExpiryBadge } from './DocumentsPanel'

const pathFor: Record<DocumentSubject, string> = {
  Property: '/app/properties',
  Owner: '/app/owners',
  Tenant: '/app/tenants',
  Lease: '/app/leases',
  Maintenance: '/app/maintenance',
}

/** Insurance, permits and other documents that lapse within 60 days, or already have. Hidden when there are none. */
export function ExpiringDocuments() {
  const list = useQuery({
    queryKey: ['documents', 'expiring'],
    queryFn: () => get<ExpiringDocument[]>('/api/v1/documents/expiring?days=60'),
  })
  if (!list.error && (list.data?.length ?? 0) === 0) return null
  return (
    <Card title="Documents expiring soon">
      <ErrorBox error={list.error} />
      <ul className="-mx-5 -my-2 divide-y divide-slate-100">
        {list.data?.slice(0, 8).map((d) => (
          <li key={d.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-2.5 text-sm">
            <span className="min-w-0">
              <Link to={`${pathFor[d.subject]}/${d.subjectId}`} className="font-medium text-slate-900 hover:underline">
                {d.title}
              </Link>
              <span className="block truncate text-slate-500">
                {enumLabel(d.category)} · {d.subjectLabel}
              </span>
            </span>
            <ExpiryBadge expiresOn={d.expiresOn} />
          </li>
        ))}
      </ul>
    </Card>
  )
}
