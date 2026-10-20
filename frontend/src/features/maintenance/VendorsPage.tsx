import { useState } from 'react'
import { Link } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { keepPreviousData, useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Paged, Vendor } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { useDebounced } from '../../lib/hooks'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, Pager, SearchInput, TextAreaField, TextField } from '../../components/ui'

const PAGE_SIZE = 25

/** Suppliers and contractors who do the repair work. */
export function VendorsPage() {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const canManage = can(Permissions.maintenanceManage)
  const [editing, setEditing] = useState<Vendor | 'new' | null>(null)
  const [search, setSearch] = useState('')
  const [showInactive, setShowInactive] = useState(false)
  const [page, setPage] = useState(1)
  const term = useDebounced(search.trim())
  const params = new URLSearchParams({ page: String(page), pageSize: String(PAGE_SIZE), includeInactive: String(showInactive) })
  if (term) params.set('search', term)
  const vendors = useQuery({
    queryKey: ['vendors', 'list', term, showInactive, page],
    queryFn: () => get<Paged<Vendor>>(`/api/v1/vendors?${params}`),
    placeholderData: keepPreviousData,
  })
  const toggle = useMutation({
    mutationFn: (v: Vendor) => put<Vendor>(`/api/v1/vendors/${v.id}/active`, { isActive: !v.isActive }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['vendors'] }),
  })

  return (
    <>
      <Link to="/app/maintenance" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
        ← Maintenance
      </Link>
      <PageHeader
        title="Vendors"
        description="Plumbers, electricians and other contractors you give repair jobs to."
        actions={canManage && editing === null && <Button onClick={() => setEditing('new')}>Add vendor</Button>}
      />
      {editing !== null && <VendorForm vendor={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      <Card>
        <SearchInput
          label="Search by name, trade, phone or email"
          value={search}
          onChange={(v) => {
            setSearch(v)
            setPage(1)
          }}
        />
        <label className="mb-3 flex items-center gap-2 text-sm text-slate-600">
          <input
            type="checkbox"
            checked={showInactive}
            onChange={(e) => {
              setShowInactive(e.target.checked)
              setPage(1)
            }}
          />
          Show inactive vendors
        </label>
        <ErrorBox error={vendors.error ?? toggle.error} />
        {vendors.data?.items.length === 0 && <Empty>{term ? 'No vendors match.' : 'No vendors yet.'}</Empty>}
        <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
          {vendors.data?.items.map((v) => (
            <li key={v.id} className="flex flex-wrap items-start justify-between gap-3 px-5 py-3 text-sm">
              <div className="min-w-0">
                <p className="font-medium text-slate-900">
                  {v.name} {v.trade && <span className="font-normal text-slate-500">· {v.trade}</span>}
                </p>
                <p className="text-slate-600">{[v.contactName, v.phone, v.email].filter(Boolean).join(' · ')}</p>
                {v.notes && <p className="text-xs text-slate-500">{v.notes}</p>}
              </div>
              <div className="flex flex-wrap items-center gap-2">
                <span className="text-slate-600">
                  {v.openJobs} open {v.openJobs === 1 ? 'job' : 'jobs'}
                </span>
                {!v.isActive && <Badge>Inactive</Badge>}
                {canManage && (
                  <>
                    <Button variant="ghost" aria-label={`Edit ${v.name}`} onClick={() => setEditing(v)}>
                      Edit
                    </Button>
                    <Button variant="ghost" onClick={() => toggle.mutate(v)} disabled={toggle.isPending}>
                      {v.isActive ? 'Deactivate' : 'Reactivate'}
                    </Button>
                  </>
                )}
              </div>
            </li>
          ))}
        </ul>
        {vendors.data && <Pager page={page} pageSize={PAGE_SIZE} totalCount={vendors.data.totalCount} onPage={setPage} />}
      </Card>
    </>
  )
}

const schema = z
  .object({
    name: z.string().trim().min(2, 'Enter the vendor’s name.').max(200),
    trade: optionalText(100),
    contactName: optionalText(200),
    phone: optionalText(30),
    email: optionalEmail,
    address: optionalText(500),
    notes: optionalText(2000),
  })
  .refine((v) => v.phone.trim() !== '' || v.email.trim() !== '', { path: ['phone'], message: 'Enter a phone number or email.' })
type Values = z.infer<typeof schema>

function VendorForm({ vendor, onDone }: { vendor: Vendor | null; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      name: vendor?.name ?? '',
      trade: vendor?.trade ?? '',
      contactName: vendor?.contactName ?? '',
      phone: vendor?.phone ?? '',
      email: vendor?.email ?? '',
      address: vendor?.address ?? '',
      notes: vendor?.notes ?? '',
    },
  })
  const save = useMutation({
    mutationFn: (v: Values) => {
      const body = {
        name: v.name.trim(),
        trade: blankToNull(v.trade),
        contactName: blankToNull(v.contactName),
        phone: blankToNull(v.phone),
        email: blankToNull(v.email),
        address: blankToNull(v.address),
        notes: blankToNull(v.notes),
      }
      return vendor ? put<Vendor>(`/api/v1/vendors/${vendor.id}`, body) : post<Vendor>('/api/v1/vendors', body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['vendors'] })
      onDone()
    },
  })
  const errors = formState.errors
  return (
    <div className="mb-6">
      <Card title={vendor ? `Edit ${vendor.name}` : 'New vendor'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Name" {...register('name')} error={errors.name?.message} />
            <TextField label="Trade" placeholder="Plumbing, electrical…" {...register('trade')} error={errors.trade?.message} />
            <TextField label="Contact person" {...register('contactName')} error={errors.contactName?.message} />
            <TextField label="Phone" type="tel" {...register('phone')} error={errors.phone?.message} />
            <TextField label="Email" type="email" {...register('email')} error={errors.email?.message} />
            <TextField label="Address" {...register('address')} error={errors.address?.message} />
          </div>
          <TextAreaField label="Notes" rows={2} {...register('notes')} error={errors.notes?.message} />
          <div className="flex gap-2">
            <Button type="submit" disabled={save.isPending}>
              Save vendor
            </Button>
            <Button type="button" variant="secondary" onClick={onDone}>
              Cancel
            </Button>
          </div>
        </form>
      </Card>
    </div>
  )
}
