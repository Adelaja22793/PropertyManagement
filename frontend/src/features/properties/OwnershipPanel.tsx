import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, put } from '../../lib/api'
import type { Ownership, OwnerSummary, Paged } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { formatDay, todayInLagos } from '../../lib/format'
import { Button, Card, Empty, ErrorBox } from '../../components/ui'
import { ownershipProblem, ownershipTotal, type ShareRow } from './ownership'

/** Current owners of a property with their shares, the history, and (with permission) an editor. */
export function OwnershipPanel({ propertyId, archived }: { propertyId: string; archived: boolean }) {
  const { can } = useSession()
  const canEdit = !archived && can(Permissions.propertiesUpdate) && can(Permissions.ownersView)
  const canOpenOwner = can(Permissions.ownersView)
  const [editing, setEditing] = useState(false)
  const [showHistory, setShowHistory] = useState(false)
  const ownership = useQuery({
    queryKey: ['properties', propertyId, 'owners'],
    queryFn: () => get<Ownership>(`/api/v1/properties/${propertyId}/owners`),
  })
  const current = ownership.data?.current ?? []
  // Everything else: earlier owners, and any change scheduled for a later date.
  const past =
    ownership.data?.history.filter((h) => !current.some((c) => c.ownerId === h.ownerId && c.effectiveFrom === h.effectiveFrom)) ?? []

  return (
    <Card
      title="Owners"
      actions={
        canEdit &&
        !editing && (
          <Button variant="secondary" onClick={() => setEditing(true)}>
            {current.length ? 'Change owners' : 'Set owners'}
          </Button>
        )
      }
    >
      <ErrorBox error={ownership.error} />
      {editing && (
        <OwnershipEditor
          propertyId={propertyId}
          initial={current.map((c) => ({ ownerId: c.ownerId, percent: String(c.sharePercent) }))}
          onDone={() => setEditing(false)}
        />
      )}
      {!editing && ownership.data && current.length === 0 && <Empty>No owners recorded yet.</Empty>}
      {!editing && current.length > 0 && (
        <ul className="divide-y divide-slate-100">
          {current.map((c) => (
            <li key={c.ownerId} className="flex items-center justify-between gap-4 py-2.5 text-sm">
              <div className="min-w-0">
                {canOpenOwner ? (
                  <Link to={`/app/owners/${c.ownerId}`} className="font-medium text-brand-700 hover:underline">
                    {c.ownerName}
                  </Link>
                ) : (
                  <span className="font-medium">{c.ownerName}</span>
                )}
                <p className="text-xs text-slate-500">Since {formatDay(c.effectiveFrom)}</p>
              </div>
              <span className="font-semibold tabular-nums">{c.sharePercent}%</span>
            </li>
          ))}
        </ul>
      )}
      {past.length > 0 && (
        <div className="mt-4 border-t border-slate-100 pt-3">
          <button className="text-sm font-medium text-brand-700 hover:underline" onClick={() => setShowHistory((s) => !s)} aria-expanded={showHistory}>
            {showHistory ? 'Hide history' : `Show history (${past.length})`}
          </button>
          {showHistory && (
            <ul className="mt-2 divide-y divide-slate-100">
              {past.map((h, i) => (
                <li key={`${h.ownerId}-${h.effectiveFrom}-${i}`} className="flex flex-wrap justify-between gap-x-4 py-2 text-sm text-slate-600">
                  <span>
                    {h.ownerName} · {h.sharePercent}%
                  </span>
                  <span className="text-xs">
                    {formatDay(h.effectiveFrom)} – {h.effectiveTo ? formatDay(h.effectiveTo) : 'onwards'}
                  </span>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </Card>
  )
}

/** Picks owners and their percentages from a date. Saving is enabled only at exactly 100%. */
export function OwnershipEditor({ propertyId, initial, onDone }: { propertyId: string; initial: ShareRow[]; onDone: () => void }) {
  const queryClient = useQueryClient()
  const [rows, setRows] = useState<ShareRow[]>(initial.length ? initial : [{ ownerId: '', percent: '100' }])
  const [effectiveFrom, setEffectiveFrom] = useState(todayInLagos())
  const owners = useQuery({
    queryKey: ['owners', 'picker'],
    queryFn: () => get<Paged<OwnerSummary>>('/api/v1/owners?pageSize=100'),
  })
  const save = useMutation({
    mutationFn: () =>
      put<Ownership>(`/api/v1/properties/${propertyId}/owners`, {
        effectiveFrom,
        shares: rows.map((r) => ({ ownerId: r.ownerId, sharePercent: Number(r.percent) })),
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['properties'] })
      await queryClient.invalidateQueries({ queryKey: ['owners'] })
      onDone()
    },
  })

  const total = ownershipTotal(rows)
  const problem = ownershipProblem(rows) ?? (effectiveFrom ? null : 'Choose the date the change takes effect.')
  const update = (index: number, change: Partial<ShareRow>) => setRows((rs) => rs.map((r, i) => (i === index ? { ...r, ...change } : r)))

  return (
    <form
      className="space-y-4"
      onSubmit={(e) => {
        e.preventDefault()
        if (!problem) save.mutate()
      }}
      noValidate
    >
      <ErrorBox error={save.error ?? owners.error} />
      {owners.data?.items.length === 0 && (
        <p className="text-sm text-slate-600">
          No owners yet.{' '}
          <Link to="/app/owners" className="font-medium text-brand-700 hover:underline">
            Add an owner
          </Link>{' '}
          first.
        </p>
      )}
      <div className="max-w-xs space-y-1">
        <label htmlFor="ownership-from" className="block text-sm font-medium text-slate-700">
          Effective from
        </label>
        <input
          id="ownership-from"
          type="date"
          value={effectiveFrom}
          onChange={(e) => setEffectiveFrom(e.target.value)}
          className="block w-full rounded-md border-0 px-3 py-2 text-sm ring-1 ring-slate-300 focus:ring-2 focus:ring-brand-500"
        />
      </div>
      <ul className="space-y-3">
        {rows.map((row, index) => (
          <li key={index} className="flex flex-wrap items-end gap-2">
            <div className="min-w-0 flex-1 basis-48 space-y-1">
              <label htmlFor={`owner-${index}`} className="block text-xs font-medium text-slate-600">
                Owner {index + 1}
              </label>
              <select
                id={`owner-${index}`}
                value={row.ownerId}
                onChange={(e) => update(index, { ownerId: e.target.value })}
                className="block w-full rounded-md border-0 px-3 py-2 text-sm ring-1 ring-slate-300 focus:ring-2 focus:ring-brand-500"
              >
                <option value="">Choose an owner…</option>
                {owners.data?.items.map((o) => (
                  <option key={o.id} value={o.id}>
                    {o.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="w-28 space-y-1">
              <label htmlFor={`share-${index}`} className="block text-xs font-medium text-slate-600">
                Share %
              </label>
              <input
                id={`share-${index}`}
                inputMode="decimal"
                value={row.percent}
                onChange={(e) => update(index, { percent: e.target.value })}
                className="block w-full rounded-md border-0 px-3 py-2 text-right text-sm tabular-nums ring-1 ring-slate-300 focus:ring-2 focus:ring-brand-500"
              />
            </div>
            <Button
              type="button"
              variant="ghost"
              aria-label={`Remove owner ${index + 1}`}
              disabled={rows.length === 1}
              onClick={() => setRows((rs) => rs.filter((_, i) => i !== index))}
            >
              Remove
            </Button>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 pt-3">
        <Button type="button" variant="secondary" onClick={() => setRows((rs) => [...rs, { ownerId: '', percent: '' }])}>
          Add another owner
        </Button>
        <p className={`text-sm font-semibold tabular-nums ${total === 100 ? 'text-brand-700' : 'text-amber-700'}`} data-testid="ownership-total">
          Total: {total}%
        </p>
      </div>
      {problem && <p className="text-xs text-slate-600">{problem}</p>}
      <div className="flex gap-2">
        <Button type="submit" disabled={!!problem || save.isPending}>
          Save owners
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
