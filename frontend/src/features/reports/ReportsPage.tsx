import { useState } from 'react'
import { useSearchParams } from 'react-router-dom'
import { keepPreviousData, useMutation, useQuery } from '@tanstack/react-query'
import { downloadFile, get } from '../../lib/api'
import type { Branch, Paged, PropertySummary, ReportDefinition, ReportTable } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { formatDate, formatDay, todayInLagos } from '../../lib/format'
import { Button, Card, Empty, ErrorBox, PageHeader } from '../../components/ui'
import { alignRight, formatCell } from './reportFormat'

/** Operational reports (design 6.14). Money reports arrive with the finance work. */
export function ReportsPage() {
  const [params, setParams] = useSearchParams()
  const definitions = useQuery({ queryKey: ['reports'], queryFn: () => get<ReportDefinition[]>('/api/v1/reports') })
  const list = definitions.data ?? []
  const current = list.find((d) => d.key === params.get('report')) ?? list[0]

  return (
    <>
      <PageHeader
        title="Reports"
        description="Occupancy, leases, repairs and letting, for the properties you can see. Download any report as a spreadsheet."
      />
      <ErrorBox error={definitions.error} />
      {definitions.data?.length === 0 && <Empty>You don't have access to any reports.</Empty>}
      {current && (
        <div className="grid gap-6 lg:grid-cols-[14rem_minmax(0,1fr)]">
          <nav aria-label="Reports">
            <label htmlFor="report-picker" className="sr-only">
              Report
            </label>
            <select
              id="report-picker"
              className="block w-full rounded-md border-0 px-3 py-2 text-sm ring-1 ring-slate-300 lg:hidden"
              value={current.key}
              onChange={(e) => setParams({ report: e.target.value })}
            >
              {list.map((d) => (
                <option key={d.key} value={d.key}>
                  {d.title}
                </option>
              ))}
            </select>
            <ul className="hidden space-y-1 lg:block">
              {list.map((d) => (
                <li key={d.key}>
                  <button
                    type="button"
                    onClick={() => setParams({ report: d.key })}
                    aria-current={d.key === current.key ? 'page' : undefined}
                    className={`block w-full rounded-md px-3 py-2 text-left text-sm font-medium ${
                      d.key === current.key ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-100'
                    }`}
                  >
                    {d.title}
                  </button>
                </li>
              ))}
            </ul>
          </nav>
          <Report key={current.key} definition={current} />
        </div>
      )}
    </>
  )
}

const inputClass = 'block w-full rounded-md border-0 px-3 py-2 text-sm ring-1 ring-slate-300 focus:ring-2 focus:ring-brand-500'

function Report({ definition }: { definition: ReportDefinition }) {
  const { can } = useSession()
  const [from, setFrom] = useState('')
  const [to, setTo] = useState('')
  const [propertyId, setPropertyId] = useState('')
  const [branchId, setBranchId] = useState('')
  const query = new URLSearchParams()
  if (definition.usesDates && from) query.set('from', from)
  if (definition.usesDates && to) query.set('to', to)
  if (definition.usesProperty && propertyId) query.set('propertyId', propertyId)
  if (definition.usesBranch && branchId) query.set('branchId', branchId)
  const path = `/api/v1/reports/${definition.key}?${query}`

  const report = useQuery({
    queryKey: ['reports', definition.key, query.toString()],
    queryFn: () => get<ReportTable>(path),
    placeholderData: keepPreviousData,
  })
  const properties = useQuery({
    queryKey: ['properties', 'options'],
    queryFn: () => get<Paged<PropertySummary>>('/api/v1/properties?pageSize=100'),
    enabled: definition.usesProperty,
  })
  const canBranches = definition.usesBranch && can(Permissions.branchesView)
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => get<Branch[]>('/api/v1/branches'), enabled: canBranches })
  const download = useMutation({
    mutationFn: () => downloadFile(`${path}${query.size ? '&' : ''}format=csv`, `${definition.key}-${todayInLagos()}.csv`),
  })
  const r = report.data

  return (
    <div className="min-w-0 space-y-4">
      <Card>
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="text-base font-semibold text-slate-900">{definition.title}</h2>
            <p className="text-sm text-slate-600">{definition.description}</p>
          </div>
          <Button variant="secondary" onClick={() => download.mutate()} disabled={download.isPending || !r}>
            {download.isPending ? 'Preparing…' : 'Download CSV'}
          </Button>
        </div>
        {(definition.usesDates || definition.usesProperty || canBranches) && (
          <div className="mt-4 grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
            {definition.usesDates && (
              <>
                <label className="space-y-1 text-sm font-medium text-slate-700">
                  <span>From</span>
                  <input type="date" className={inputClass} value={from || r?.from || ''} onChange={(e) => setFrom(e.target.value)} />
                </label>
                <label className="space-y-1 text-sm font-medium text-slate-700">
                  <span>To</span>
                  <input type="date" className={inputClass} value={to || r?.to || ''} onChange={(e) => setTo(e.target.value)} />
                </label>
              </>
            )}
            {definition.usesProperty && (
              <label className="space-y-1 text-sm font-medium text-slate-700">
                <span>Property</span>
                <select className={inputClass} value={propertyId} onChange={(e) => setPropertyId(e.target.value)}>
                  <option value="">All properties</option>
                  {properties.data?.items.map((p) => (
                    <option key={p.id} value={p.id}>
                      {p.name} ({p.code})
                    </option>
                  ))}
                </select>
              </label>
            )}
            {canBranches && (
              <label className="space-y-1 text-sm font-medium text-slate-700">
                <span>Branch</span>
                <select className={inputClass} value={branchId} onChange={(e) => setBranchId(e.target.value)}>
                  <option value="">All branches</option>
                  {branches.data?.map((b) => (
                    <option key={b.id} value={b.id}>
                      {b.name}
                    </option>
                  ))}
                </select>
              </label>
            )}
          </div>
        )}
        <div className="mt-3 space-y-3">
          <ErrorBox error={report.error ?? download.error} />
        </div>
      </Card>

      {r && (
        <>
          {r.facts.length > 0 && (
            <dl className="grid grid-cols-2 gap-3 sm:grid-cols-3">
              {r.facts.map((f) => (
                <div key={f.label} className="rounded-lg bg-white p-4 shadow-sm ring-1 ring-slate-200">
                  <dt className="text-xs font-medium text-slate-500">{f.label}</dt>
                  <dd className="mt-1 text-xl font-semibold text-slate-900">{f.value}</dd>
                </div>
              ))}
            </dl>
          )}
          <Card>
            {r.rows.length === 0 ? (
              <Empty>Nothing to show for this period.</Empty>
            ) : (
              <div className="-mx-5 -my-5 overflow-x-auto">
                <table className="min-w-full text-sm">
                  <thead className="bg-slate-50 text-left text-xs font-medium text-slate-500">
                    <tr>
                      {r.columns.map((c) => (
                        <th key={c.key} scope="col" className={`px-4 py-2.5 whitespace-nowrap ${alignRight(c.kind) ? 'text-right' : ''}`}>
                          {c.label}
                        </th>
                      ))}
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-slate-100">
                    {r.rows.map((row, i) => (
                      <tr key={i}>
                        {row.map((cell, j) => (
                          <td
                            key={r.columns[j].key}
                            className={`px-4 py-2.5 whitespace-nowrap ${alignRight(r.columns[j].kind) ? 'text-right tabular-nums' : ''}`}
                          >
                            {formatCell(cell, r.columns[j].kind)}
                          </td>
                        ))}
                      </tr>
                    ))}
                  </tbody>
                  {r.totals && (
                    <tfoot className="border-t border-slate-200 font-semibold text-slate-900">
                      <tr>
                        {r.totals.map((cell, j) => (
                          <td
                            key={r.columns[j].key}
                            className={`px-4 py-2.5 whitespace-nowrap ${alignRight(r.columns[j].kind) ? 'text-right tabular-nums' : ''}`}
                          >
                            {cell === null ? '' : formatCell(cell, r.columns[j].kind)}
                          </td>
                        ))}
                      </tr>
                    </tfoot>
                  )}
                </table>
              </div>
            )}
          </Card>
          <p className="text-xs text-slate-500">
            {r.from && r.to ? `${formatDay(r.from)} to ${formatDay(r.to)} · ` : ''}
            {r.rows.length} {r.rows.length === 1 ? 'row' : 'rows'} · prepared {formatDate(r.generatedAtUtc)}
            {r.truncated && ' · only the first 5,000 rows are shown; narrow the filters to see the rest'}
          </p>
        </>
      )}
    </div>
  )
}
