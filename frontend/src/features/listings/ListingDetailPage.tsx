import { useState } from 'react'
import { Link, useNavigate, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { documentUrl, get, post, put } from '../../lib/api'
import type { LeadDetail, ListingDetail, ListingPhotoOption } from '../../lib/types'
import { LeadSources } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate, formatDay } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Badge, Button, Card, DetailList, Empty, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { LeadList } from './ListingsPage'
import { publicListingPath, rentLabel, unitSummary } from './listingFormat'

export function ListingDetailPage() {
  const { id = '' } = useParams()
  const { can } = useSession()
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const canManage = can(Permissions.listingsManage)
  const canLeads = can(Permissions.leadsManage)
  const listing = useQuery({ queryKey: ['listings', id], queryFn: () => get<ListingDetail>(`/api/v1/listings/${id}`) })
  const action = useMutation({
    mutationFn: ({ path, body }: { path: string; body?: unknown }) => post<ListingDetail>(`/api/v1/listings/${id}/${path}`, body),
    onSuccess: (saved) => {
      queryClient.setQueryData(['listings', id], saved)
      return queryClient.invalidateQueries({ queryKey: ['listings', 'list'] })
    },
  })

  const l = listing.data
  if (!l) return <ErrorBox error={listing.error} />
  const publicPath = publicListingPath(l.publicCode)

  return (
    <>
      <Link to="/app/listings" className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
        ← Listings
      </Link>
      <PageHeader
        title={l.headline}
        description={`${l.propertyName}, unit ${l.unitCode} · ${rentLabel(l.askingRent, l.billingFrequency)}`}
        actions={
          canManage && (
            <div className="flex flex-wrap gap-2">
              {l.status !== 'Closed' && (
                <Button variant="secondary" onClick={() => navigate(`/app/listings/${l.id}/edit`)}>
                  Edit
                </Button>
              )}
              {l.status === 'Draft' && (
                <Button onClick={() => action.mutate({ path: 'publish' })} disabled={action.isPending}>
                  Publish
                </Button>
              )}
              {l.status === 'Published' && (
                <Button variant="secondary" onClick={() => action.mutate({ path: 'unpublish' })} disabled={action.isPending}>
                  Unpublish
                </Button>
              )}
              {l.status !== 'Closed' && (
                <>
                  <Button
                    variant="secondary"
                    onClick={() => action.mutate({ path: 'close', body: { reason: 'Let' } })}
                    disabled={action.isPending}
                  >
                    Mark as let
                  </Button>
                  <Button
                    variant="danger"
                    onClick={() => action.mutate({ path: 'close', body: { reason: 'Withdrawn' } })}
                    disabled={action.isPending}
                  >
                    Withdraw
                  </Button>
                </>
              )}
              {l.status === 'Closed' && (
                <Button variant="secondary" onClick={() => action.mutate({ path: 'reopen' })} disabled={action.isPending}>
                  Reopen
                </Button>
              )}
            </div>
          )
        }
      />
      <div className="space-y-6">
        <ErrorBox error={action.error} />
        {l.status === 'Published' && (
          <div className="flex flex-wrap items-center gap-2 rounded-lg bg-brand-50 px-4 py-3 text-sm text-brand-700 ring-1 ring-brand-100">
            <span>Live on your public page:</span>
            <a href={publicPath} target="_blank" rel="noreferrer" className="font-medium break-all underline">
              {window.location.origin}
              {publicPath}
            </a>
          </div>
        )}
        <Card title="Listing">
          <DetailList
            items={[
              {
                label: 'Status',
                value: <Badge tone={statusTone(l.closeReason ?? l.status)}>{l.closeReason ? `Closed · ${l.closeReason}` : l.status}</Badge>,
              },
              { label: 'Space', value: unitSummary(l.unitType, l.bedrooms, l.bathrooms) },
              { label: 'Available from', value: l.availableFrom ? formatDay(l.availableFrom) : 'Now' },
              { label: 'Address shown', value: l.showStreetAddress ? l.propertyAddress : 'Area and state only' },
              { label: 'Contact on listing', value: [l.contactName, l.contactPhone].filter(Boolean).join(' · ') || "Company's phone" },
              { label: 'Agent', value: l.agentName },
              { label: 'Unit now', value: l.unitOccupancy },
              { label: 'Published', value: l.publishedAtUtc && formatDate(l.publishedAtUtc) },
            ]}
          />
          {l.description && <p className="mt-4 text-sm whitespace-pre-line text-slate-700">{l.description}</p>}
          {l.features && (
            <ul className="mt-3 flex flex-wrap gap-2">
              {l.features
                .split('\n')
                .map((f) => f.trim())
                .filter(Boolean)
                .map((f) => (
                  <li key={f}>
                    <Badge>{f}</Badge>
                  </li>
                ))}
            </ul>
          )}
        </Card>
        <PhotosCard listing={l} canManage={canManage && l.status !== 'Closed'} />
        {canLeads && (
          <Card title="Enquiries">
            <AddLead listingId={l.id} />
            <LeadList listingId={l.id} />
          </Card>
        )}
      </div>
    </>
  )
}

/** The listing's photos, chosen from the property's photos and shown in this order. */
function PhotosCard({ listing, canManage }: { listing: ListingDetail; canManage: boolean }) {
  const queryClient = useQueryClient()
  const [picking, setPicking] = useState<string[] | null>(null)
  const options = useQuery({
    queryKey: ['listings', listing.id, 'photo-options'],
    queryFn: () => get<ListingPhotoOption[]>(`/api/v1/listings/${listing.id}/photo-options`),
    enabled: picking !== null,
  })
  const save = useMutation({
    mutationFn: (documentIds: string[]) => put<ListingDetail>(`/api/v1/listings/${listing.id}/photos`, { documentIds }),
    onSuccess: (saved) => {
      queryClient.setQueryData(['listings', listing.id], saved)
      setPicking(null)
      return queryClient.invalidateQueries({ queryKey: ['listings', 'list'] })
    },
  })
  const toggle = (id: string) => setPicking((p) => ((p ?? []).includes(id) ? (p ?? []).filter((x) => x !== id) : [...(p ?? []), id]))
  const move = (index: number, by: number) =>
    setPicking((p) => {
      if (!p) return p
      const next = [...p]
      const [item] = next.splice(index, 1)
      next.splice(index + by, 0, item)
      return next
    })

  return (
    <Card
      title="Photos"
      actions={
        canManage &&
        picking === null && (
          <Button variant="secondary" onClick={() => setPicking(listing.photos.map((p) => p.documentId))}>
            Choose photos
          </Button>
        )
      }
    >
      {picking === null ? (
        listing.photos.length === 0 ? (
          <Empty>No photos yet. A listing needs at least one photo before it can be published.</Empty>
        ) : (
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {listing.photos.map((p, i) => (
              <li key={p.documentId} className="overflow-hidden rounded-md ring-1 ring-slate-200">
                <img src={documentUrl(p.documentId)} alt={p.title} loading="lazy" className="aspect-[4/3] w-full object-cover" />
                <p className="truncate px-2 py-1 text-xs text-slate-600">
                  {i === 0 ? 'Cover · ' : ''}
                  {p.title}
                </p>
              </li>
            ))}
          </ul>
        )
      ) : (
        <div className="space-y-4">
          <ErrorBox error={options.error ?? save.error} />
          <p className="text-sm text-slate-600">
            Tick the property photos to show, in order. The first is the cover. Upload new photos on the{' '}
            <Link to={`/app/properties/${listing.propertyId}`} className="font-medium text-brand-700 hover:underline">
              property's page
            </Link>
            .
          </p>
          {options.data?.length === 0 && <Empty>This property has no photos yet.</Empty>}
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
            {options.data?.map((o) => {
              const index = picking.indexOf(o.documentId)
              return (
                <li key={o.documentId} className={`overflow-hidden rounded-md ring-2 ${index >= 0 ? 'ring-brand-600' : 'ring-slate-200'}`}>
                  <label className="block cursor-pointer">
                    <img src={documentUrl(o.documentId)} alt="" loading="lazy" className="aspect-[4/3] w-full object-cover" />
                    <span className="flex items-center gap-2 px-2 py-1 text-xs text-slate-700">
                      <input type="checkbox" checked={index >= 0} onChange={() => toggle(o.documentId)} aria-label={`Use ${o.title}`} />
                      <span className="truncate">{index >= 0 ? `${index + 1}. ${o.title}` : o.title}</span>
                    </span>
                  </label>
                  {index >= 0 && picking.length > 1 && (
                    <div className="flex justify-end gap-1 px-2 pb-1">
                      <button
                        type="button"
                        className="rounded px-1.5 text-xs text-slate-600 hover:bg-slate-100 disabled:opacity-30"
                        disabled={index === 0}
                        onClick={() => move(index, -1)}
                        aria-label={`Move ${o.title} earlier`}
                      >
                        ←
                      </button>
                      <button
                        type="button"
                        className="rounded px-1.5 text-xs text-slate-600 hover:bg-slate-100 disabled:opacity-30"
                        disabled={index === picking.length - 1}
                        onClick={() => move(index, 1)}
                        aria-label={`Move ${o.title} later`}
                      >
                        →
                      </button>
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
          <div className="flex gap-2">
            <Button onClick={() => save.mutate(picking)} disabled={save.isPending}>
              Save photos
            </Button>
            <Button variant="secondary" onClick={() => setPicking(null)}>
              Cancel
            </Button>
          </div>
        </div>
      )}
    </Card>
  )
}

const leadSchema = z
  .object({
    name: z.string().trim().min(2, 'Enter their name.').max(200),
    phone: optionalText(30),
    email: optionalEmail,
    message: optionalText(2000),
    source: z.enum(LeadSources),
  })
  .refine((v) => v.phone.trim() !== '' || v.email.trim() !== '', { path: ['phone'], message: 'Enter a phone number or email.' })
type LeadValues = z.infer<typeof leadSchema>

/** Records an enquiry that came by phone, walk-in or referral. */
function AddLead({ listingId }: { listingId: string }) {
  const [open, setOpen] = useState(false)
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const { register, handleSubmit, formState } = useForm<LeadValues>({
    resolver: zodResolver(leadSchema),
    defaultValues: { name: '', phone: '', email: '', message: '', source: 'Phone' },
  })
  const save = useMutation({
    mutationFn: (v: LeadValues) =>
      post<LeadDetail>('/api/v1/leads', {
        listingId,
        name: v.name.trim(),
        phone: blankToNull(v.phone),
        email: blankToNull(v.email),
        message: blankToNull(v.message),
        source: v.source,
      }),
    onSuccess: async (lead) => {
      await queryClient.invalidateQueries({ queryKey: ['leads'] })
      navigate(`/app/leads/${lead.id}`)
    },
  })
  const errors = formState.errors
  if (!open)
    return (
      <div className="mb-3">
        <Button variant="secondary" onClick={() => setOpen(true)}>
          Add enquiry
        </Button>
      </div>
    )
  return (
    <form
      onSubmit={handleSubmit((v) => save.mutate(v))}
      noValidate
      className="mb-5 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200"
    >
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField label="Name" {...register('name')} error={errors.name?.message} />
        <SelectField label="How they got in touch" {...register('source')}>
          {LeadSources.filter((s) => s !== 'Website').map((s) => (
            <option key={s} value={s}>
              {enumLabel(s)}
            </option>
          ))}
        </SelectField>
        <TextField label="Phone" type="tel" {...register('phone')} error={errors.phone?.message} />
        <TextField label="Email" type="email" {...register('email')} error={errors.email?.message} />
        <div className="sm:col-span-2">
          <TextAreaField label="What they asked" {...register('message')} error={errors.message?.message} />
        </div>
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          Save enquiry
        </Button>
        <Button type="button" variant="secondary" onClick={() => setOpen(false)}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
