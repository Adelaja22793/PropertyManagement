import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { PortalRequestDetail, PortalRequestSummary } from '../../lib/types'
import { enumLabel, formatDate, formatDay, formatNaira } from '../../lib/format'
import { Badge, Button, Card, DetailList, Empty, ErrorBox, TextAreaField } from '../../components/ui'
import { StatusBadge, UrgencyBadge } from '../maintenance/MaintenanceList'

export function RequestList({ items, onOpen, empty }: { items: PortalRequestSummary[]; onOpen: (id: string) => void; empty: string }) {
  if (items.length === 0) return <Empty>{empty}</Empty>
  return (
    <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
      {items.map((r) => (
        <li key={r.id}>
          <button onClick={() => onOpen(r.id)} className="flex w-full flex-wrap items-center justify-between gap-x-4 gap-y-1 px-5 py-3 text-left hover:bg-slate-50">
            <span className="min-w-0">
              <span className="block text-sm font-medium text-slate-900">
                {r.title}
                <span className="ml-2 text-xs font-normal text-slate-500">{r.reference}</span>
              </span>
              <span className="block truncate text-sm text-slate-600">
                {r.propertyName}
                {r.unitCode && ` · Unit ${r.unitCode}`} · reported {formatDate(r.createdAtUtc)}
              </span>
            </span>
            <span className="flex flex-wrap items-center gap-2">
              {r.awaitingOwnerApproval && <Badge tone="amber">Needs your approval</Badge>}
              <StatusBadge status={r.status} />
            </span>
          </button>
        </li>
      ))}
    </ul>
  )
}

const portalStep: Record<string, string> = {
  Submitted: 'Received',
  UnderReview: 'Being reviewed',
  Approved: 'Approved',
  Assigned: 'Assigned to someone to fix',
  InProgress: 'Work started',
  AwaitingParts: 'Waiting for parts',
  Completed: 'Completed',
  Rejected: 'Not going ahead',
  Cancelled: 'Cancelled',
}

/** One request in a portal: what was reported, where it stands and the steps so far. */
export function RequestDetail({
  path,
  onBack,
  children,
}: {
  path: string
  onBack: () => void
  children?: (request: PortalRequestDetail, refresh: (r: PortalRequestDetail) => void) => ReactNode
}) {
  const queryClient = useQueryClient()
  const request = useQuery({ queryKey: ['portal', path], queryFn: () => get<PortalRequestDetail>(path) })
  const refresh = (saved: PortalRequestDetail) => {
    queryClient.setQueryData(['portal', path], saved)
    void queryClient.invalidateQueries({ queryKey: ['portal'] })
  }
  const r = request.data
  return (
    <div className="space-y-4">
      <button onClick={onBack} className="text-sm font-medium text-brand-700 hover:underline">
        ← Back
      </button>
      <ErrorBox error={request.error} />
      {r && (
        <>
          <Card title={`${r.title} · ${r.reference}`}>
            <div className="mb-4 flex flex-wrap gap-2">
              <StatusBadge status={r.status} />
              <UrgencyBadge urgency={r.urgency} />
            </div>
            <DetailList
              items={[
                { label: 'Where', value: `${r.propertyName}${r.unitCode ? `, unit ${r.unitCode}` : ''}` },
                { label: 'Category', value: enumLabel(r.category) },
                { label: 'Reported', value: formatDate(r.createdAtUtc) },
                { label: 'Contractor', value: r.vendorName },
                { label: 'Estimate', value: r.estimateAmount === null ? null : `${formatNaira(r.estimateAmount)}${r.estimateNotes ? ` · ${r.estimateNotes}` : ''}` },
                { label: "Owner's approval", value: r.ownerApprovalNote },
                { label: 'Completed', value: r.completedOn ? formatDay(r.completedOn) : null },
                { label: 'Final cost', value: r.actualCost === null ? null : formatNaira(r.actualCost) },
                { label: 'Reason', value: r.closedReason },
                ...(r.description ? [{ label: 'Details', value: r.description }] : []),
              ]}
            />
          </Card>
          {children?.(r, refresh)}
          <Card title="Progress">
            <ol className="space-y-3">
              {r.updates.map((u, i) => (
                <li key={i} className="text-sm">
                  <p className="text-slate-800">
                    {u.kind === 'Comment'
                      ? 'Your message'
                      : u.kind === 'Estimated'
                        ? 'Estimate added'
                        : u.kind === 'OwnerApproved'
                          ? "Owner's approval recorded"
                          : u.kind === 'Assigned'
                            ? 'Assigned to someone to fix'
                            : u.kind === 'Created'
                              ? 'Request received'
                              : portalStep[u.status] ?? enumLabel(u.status)}
                  </p>
                  {u.text && u.kind !== 'Estimated' && <p className="whitespace-pre-line text-slate-600">{u.text}</p>}
                  <p className="text-xs text-slate-500">{formatDate(u.atUtc)}</p>
                </li>
              ))}
            </ol>
          </Card>
        </>
      )}
    </div>
  )
}

/** A short message form used by both portals. */
export function MessageForm({
  label,
  button,
  path,
  body,
  onSaved,
  required = true,
}: {
  label: string
  button: string
  path: string
  body: (text: string) => unknown
  onSaved: (r: PortalRequestDetail) => void
  required?: boolean
}) {
  const [text, setText] = useState('')
  const send = useMutation({
    mutationFn: () => post<PortalRequestDetail>(path, body(text.trim())),
    onSuccess: (saved) => {
      setText('')
      onSaved(saved)
    },
  })
  return (
    <form
      className="space-y-2"
      onSubmit={(e) => {
        e.preventDefault()
        send.mutate()
      }}
    >
      <ErrorBox error={send.error} />
      <TextAreaField label={label} rows={2} maxLength={1000} value={text} onChange={(e) => setText(e.target.value)} />
      <Button type="submit" disabled={send.isPending || (required && !text.trim())}>
        {button}
      </Button>
    </form>
  )
}
