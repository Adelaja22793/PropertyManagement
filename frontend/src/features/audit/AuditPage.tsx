import { useState } from 'react'
import { keepPreviousData, useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { AuditLogEntry, Paged } from '../../lib/types'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader } from '../../components/ui'
import { formatDate } from '../../lib/format'

const PAGE_SIZE = 25

export function AuditPage() {
  const [page, setPage] = useState(1)
  const logs = useQuery({
    queryKey: ['audit', page],
    queryFn: () => get<Paged<AuditLogEntry>>(`/api/v1/audit-logs?page=${page}&pageSize=${PAGE_SIZE}`),
    placeholderData: keepPreviousData,
  })
  const totalPages = logs.data ? Math.max(1, Math.ceil(logs.data.totalCount / PAGE_SIZE)) : 1

  return (
    <>
      <PageHeader title="Audit log" description="Sign-ins, access changes and other sensitive actions in this organization, newest first." />
      <Card>
        <ErrorBox error={logs.error} />
        {logs.data?.items.length === 0 && <Empty>Nothing recorded yet.</Empty>}
        {!!logs.data?.items.length && (
          <div className="-mx-5 overflow-x-auto">
            <table className="w-full text-left text-sm">
              <thead className="text-xs text-slate-500 uppercase">
                <tr>
                  <th className="px-5 py-2 font-medium">When (Lagos)</th>
                  <th className="px-5 py-2 font-medium">Who</th>
                  <th className="px-5 py-2 font-medium">Action</th>
                  <th className="px-5 py-2 font-medium">Outcome</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {logs.data.items.map((l) => (
                  <tr key={l.id}>
                    <td className="px-5 py-2.5 whitespace-nowrap text-slate-600">{formatDate(l.timestampUtc)}</td>
                    <td className="px-5 py-2.5">{l.userEmail ?? 'System'}</td>
                    <td className="px-5 py-2.5 font-mono text-xs">{l.action}</td>
                    <td className="px-5 py-2.5">
                      <Badge tone={l.outcome === 'Success' ? 'green' : 'red'}>{l.outcome}</Badge>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        <div className="mt-4 flex items-center justify-between text-sm text-slate-600">
          <span>
            Page {page} of {totalPages}
          </span>
          <div className="flex gap-2">
            <Button variant="secondary" disabled={page <= 1} onClick={() => setPage((p) => p - 1)}>
              Newer
            </Button>
            <Button variant="secondary" disabled={page >= totalPages} onClick={() => setPage((p) => p + 1)}>
              Older
            </Button>
          </div>
        </div>
      </Card>
    </>
  )
}
