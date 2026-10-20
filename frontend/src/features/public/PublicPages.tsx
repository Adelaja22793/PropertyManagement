import { useEffect, useState, type ReactNode } from 'react'
import { Link, useParams } from 'react-router-dom'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery } from '@tanstack/react-query'
import { ApiError, publicGet, publicPost } from '../../lib/api'
import type { PublicListing, PublicOrganization } from '../../lib/types'
import { formatDay, todayInLagos } from '../../lib/format'
import { blankToNull, optionalEmail, optionalText } from '../../lib/validation'
import { Button, ErrorBox, TextAreaField, TextField } from '../../components/ui'
import { publicCompanyPath, publicListingPath, rentLabel, unitSummary } from '../listings/listingFormat'

/** The frame around every public page: no sign-in, no staff navigation. */
function PublicLayout({ company, children }: { company?: { name: string; slug: string }; children: ReactNode }) {
  return (
    <div className="min-h-full bg-slate-50">
      <header className="border-b border-slate-200 bg-white">
        <div className="mx-auto flex max-w-5xl items-center justify-between gap-3 px-4 py-4">
          {company ? (
            <Link to={publicCompanyPath(company.slug)} className="text-base font-semibold text-slate-900 hover:underline">
              {company.name}
            </Link>
          ) : (
            <span className="text-base font-semibold text-slate-900">To let</span>
          )}
          <span className="text-xs text-slate-500">Properties to let</span>
        </div>
      </header>
      <main className="mx-auto max-w-5xl px-4 py-6">{children}</main>
    </div>
  )
}

function NotFound({ what }: { what: string }) {
  return (
    <PublicLayout>
      <div className="rounded-lg bg-white p-8 text-center ring-1 ring-slate-200">
        <h1 className="text-lg font-semibold">This {what} isn't available</h1>
        <p className="mt-2 text-sm text-slate-600">It may have been let already or taken down.</p>
      </div>
    </PublicLayout>
  )
}

const isNotFound = (error: unknown) => error instanceof ApiError && error.status === 404

function availability(day: string | null): string {
  return !day || day <= todayInLagos() ? 'Available now' : `Available from ${formatDay(day)}`
}

/** A company's public page: every listing it has published. */
export function PublicCompanyPage() {
  const { slug = '' } = useParams()
  const company = useQuery({
    queryKey: ['public', 'company', slug],
    queryFn: () => publicGet<PublicOrganization>(`/api/v1/public/organizations/${encodeURIComponent(slug)}/listings`),
  })
  useEffect(() => {
    if (company.data) document.title = `${company.data.name}: properties to let`
  }, [company.data])

  if (isNotFound(company.error)) return <NotFound what="page" />
  if (!company.data)
    return (
      <PublicLayout>
        <ErrorBox error={company.error} />
        {!company.error && <p className="text-sm text-slate-500">Loading…</p>}
      </PublicLayout>
    )

  const c = company.data
  return (
    <PublicLayout company={c}>
      <h1 className="text-xl font-semibold text-slate-900">Properties to let</h1>
      <p className="mt-1 text-sm text-slate-600">{[c.contactPhone, c.contactEmail].filter(Boolean).join(' · ') || `Listed by ${c.name}`}</p>
      {c.listings.length === 0 ? (
        <p className="mt-8 rounded-lg bg-white p-8 text-center text-sm text-slate-500 ring-1 ring-slate-200">
          Nothing to let right now. Check back soon.
        </p>
      ) : (
        <ul className="mt-6 grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {c.listings.map((l) => (
            <li key={l.code}>
              <Link
                to={publicListingPath(l.code)}
                className="block overflow-hidden rounded-lg bg-white shadow-sm ring-1 ring-slate-200 transition hover:ring-brand-500"
              >
                <div className="aspect-[4/3] bg-slate-100">
                  {l.photoUrl && <img src={l.photoUrl} alt="" loading="lazy" className="h-full w-full object-cover" />}
                </div>
                <div className="space-y-1 p-4">
                  <p className="font-semibold text-slate-900">{l.headline}</p>
                  <p className="text-sm font-medium text-brand-700">{rentLabel(l.askingRent, l.billingFrequency)}</p>
                  <p className="text-sm text-slate-600">{unitSummary(l.unitType, l.bedrooms, l.bathrooms)}</p>
                  <p className="text-xs text-slate-500">
                    {l.area} · {availability(l.availableFrom)}
                  </p>
                </div>
              </Link>
            </li>
          ))}
        </ul>
      )}
    </PublicLayout>
  )
}

/** One listing with its photos, details and the enquiry form. */
export function PublicListingPage() {
  const { code = '' } = useParams()
  const listing = useQuery({
    queryKey: ['public', 'listing', code],
    queryFn: () => publicGet<PublicListing>(`/api/v1/public/listings/${encodeURIComponent(code)}`),
  })
  const [photo, setPhoto] = useState(0)
  useEffect(() => {
    if (listing.data) document.title = `${listing.data.headline} · ${listing.data.organizationName}`
  }, [listing.data])

  if (isNotFound(listing.error)) return <NotFound what="listing" />
  if (!listing.data)
    return (
      <PublicLayout>
        <ErrorBox error={listing.error} />
        {!listing.error && <p className="text-sm text-slate-500">Loading…</p>}
      </PublicLayout>
    )

  const l = listing.data
  const facts = [
    unitSummary(l.unitType, l.bedrooms, l.bathrooms),
    l.sizeSquareMetres ? `${l.sizeSquareMetres} m²` : null,
    availability(l.availableFrom),
  ].filter(Boolean)
  return (
    <PublicLayout company={{ name: l.organizationName, slug: l.organizationSlug }}>
      <Link to={publicCompanyPath(l.organizationSlug)} className="mb-3 inline-block text-sm font-medium text-brand-700 hover:underline">
        ← All properties
      </Link>
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <div className="min-w-0 space-y-5">
          {l.photoUrls.length > 0 && (
            <div>
              <div className="aspect-[4/3] overflow-hidden rounded-lg bg-slate-100">
                <img
                  src={l.photoUrls[photo] ?? l.photoUrls[0]}
                  alt={`Photo ${photo + 1} of ${l.headline}`}
                  className="h-full w-full object-cover"
                />
              </div>
              {l.photoUrls.length > 1 && (
                <div className="mt-2 flex gap-2 overflow-x-auto pb-1">
                  {l.photoUrls.map((url, i) => (
                    <button
                      key={url}
                      type="button"
                      onClick={() => setPhoto(i)}
                      aria-label={`Show photo ${i + 1}`}
                      aria-pressed={i === photo}
                      className={`h-16 w-20 shrink-0 overflow-hidden rounded-md ring-2 ${i === photo ? 'ring-brand-600' : 'ring-transparent'}`}
                    >
                      <img src={url} alt="" loading="lazy" className="h-full w-full object-cover" />
                    </button>
                  ))}
                </div>
              )}
            </div>
          )}
          <div>
            <h1 className="text-2xl font-semibold text-slate-900">{l.headline}</h1>
            <p className="mt-1 text-lg font-medium text-brand-700">{rentLabel(l.askingRent, l.billingFrequency)}</p>
            <p className="mt-1 text-sm text-slate-600">{l.streetAddress ? `${l.streetAddress}, ${l.area}` : l.area}</p>
            <p className="mt-2 text-sm text-slate-700">{facts.join(' · ')}</p>
          </div>
          {l.description && <p className="text-sm leading-6 whitespace-pre-line text-slate-800">{l.description}</p>}
          {l.features.length > 0 && (
            <div>
              <h2 className="text-sm font-semibold text-slate-800">Features</h2>
              <ul className="mt-2 grid gap-1 text-sm text-slate-700 sm:grid-cols-2">
                {l.features.map((f) => (
                  <li key={f}>✓ {f}</li>
                ))}
              </ul>
            </div>
          )}
        </div>
        <aside className="space-y-4">
          {l.contactPhone && (
            <div className="rounded-lg bg-white p-4 text-sm shadow-sm ring-1 ring-slate-200">
              <p className="text-slate-600">{l.contactName ? `Call ${l.contactName}` : `Call ${l.organizationName}`}</p>
              <a
                href={`tel:${l.contactPhone.replace(/\s/g, '')}`}
                className="mt-1 block text-lg font-semibold text-brand-700 hover:underline"
              >
                {l.contactPhone}
              </a>
            </div>
          )}
          <EnquiryForm code={l.code} />
        </aside>
      </div>
    </PublicLayout>
  )
}

const enquirySchema = z
  .object({
    name: z.string().trim().min(2, 'Enter your name.').max(200),
    phone: optionalText(30),
    email: optionalEmail,
    message: optionalText(2000),
    website: z.string(),
  })
  .refine((v) => v.phone.trim() !== '' || v.email.trim() !== '', {
    path: ['phone'],
    message: 'Enter a phone number or email so we can reply.',
  })
type EnquiryValues = z.infer<typeof enquirySchema>

function EnquiryForm({ code }: { code: string }) {
  const { register, handleSubmit, formState } = useForm<EnquiryValues>({
    resolver: zodResolver(enquirySchema),
    defaultValues: { name: '', phone: '', email: '', message: '', website: '' },
  })
  const send = useMutation({
    mutationFn: (v: EnquiryValues) =>
      publicPost<void>(`/api/v1/public/listings/${encodeURIComponent(code)}/enquiries`, {
        name: v.name.trim(),
        phone: blankToNull(v.phone),
        email: blankToNull(v.email),
        message: blankToNull(v.message),
        website: blankToNull(v.website),
      }),
  })
  const errors = formState.errors

  if (send.isSuccess)
    return (
      <div role="status" className="rounded-lg bg-brand-50 p-4 text-sm text-brand-700 ring-1 ring-brand-100">
        <p className="font-semibold">Thank you, your enquiry has been sent.</p>
        <p className="mt-1">Someone will contact you soon to arrange a viewing.</p>
      </div>
    )

  return (
    <form
      onSubmit={handleSubmit((v) => send.mutate(v))}
      noValidate
      className="space-y-3 rounded-lg bg-white p-4 shadow-sm ring-1 ring-slate-200"
    >
      <h2 className="text-sm font-semibold text-slate-800">Ask about this property</h2>
      <ErrorBox error={send.error} />
      <TextField label="Your name" autoComplete="name" {...register('name')} error={errors.name?.message} />
      <TextField label="Phone" type="tel" autoComplete="tel" {...register('phone')} error={errors.phone?.message} />
      <TextField label="Email" type="email" autoComplete="email" {...register('email')} error={errors.email?.message} />
      <TextAreaField
        label="Message"
        rows={3}
        placeholder="When would you like to view it?"
        {...register('message')}
        error={errors.message?.message}
      />
      {/* Hidden from people; automated spam fills it in and is quietly dropped. */}
      <div aria-hidden="true" className="absolute -left-[9999px] h-px w-px overflow-hidden">
        <label htmlFor="website">Website</label>
        <input id="website" type="text" tabIndex={-1} autoComplete="off" {...register('website')} />
      </div>
      <Button type="submit" className="w-full" disabled={send.isPending}>
        {send.isPending ? 'Sending…' : 'Send enquiry'}
      </Button>
      <p className="text-xs text-slate-500">We only use your details to reply about this property.</p>
    </form>
  )
}
