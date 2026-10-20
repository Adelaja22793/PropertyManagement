import { useState, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, del, documentUrl, get, put, upload } from '../../lib/api'
import type { DocumentCategory, DocumentItem, DocumentList, DocumentSubject } from '../../lib/types'
import { DocumentCategories } from '../../lib/types'
import { enumLabel, formatDate, formatDay } from '../../lib/format'
import { Badge, Button, Card, Empty, ErrorBox, SelectField, TextField } from '../../components/ui'
import { acceptedFiles, expiryState, formatSize, maxUploadBytes } from './documentFormat'

/** Which portal can be offered a document on each kind of record. */
const shareable: Record<DocumentSubject, { owner: boolean; tenant: boolean }> = {
  Property: { owner: true, tenant: false },
  Owner: { owner: true, tenant: false },
  Tenant: { owner: false, tenant: true },
  Lease: { owner: true, tenant: true },
  Maintenance: { owner: true, tenant: true },
}

/**
 * Documents and photos filed against a record. Hidden when the member can't see documents here; the server checks
 * every request, this only keeps the page tidy.
 */
export function DocumentsPanel({
  subject,
  subjectId,
  title = 'Documents',
  defaultCategory = 'Other',
  readOnly = false,
}: {
  subject: DocumentSubject
  subjectId: string
  title?: string
  defaultCategory?: DocumentCategory
  readOnly?: boolean
}) {
  const [adding, setAdding] = useState(false)
  const [editingId, setEditingId] = useState<string | null>(null)
  const key = ['documents', subject, subjectId]
  const list = useQuery({
    queryKey: key,
    queryFn: () => get<DocumentList>(`/api/v1/documents?subject=${subject}&subjectId=${subjectId}`),
    retry: false,
  })
  if (list.error instanceof ApiError && (list.error.status === 403 || list.error.status === 404)) return null
  const data = list.data
  const photos = data?.items.filter((d) => d.contentType.startsWith('image/')) ?? []
  const files = data?.items.filter((d) => !d.contentType.startsWith('image/')) ?? []
  const editing = data?.items.find((d) => d.id === editingId)

  return (
    <Card
      title={title}
      actions={
        data?.canUpload &&
        !readOnly &&
        !adding && (
          <Button variant="secondary" onClick={() => setAdding(true)}>
            Add file
          </Button>
        )
      }
    >
      <ErrorBox error={list.error} />
      {adding && data && (
        <UploadForm
          subject={subject}
          subjectId={subjectId}
          defaultCategory={defaultCategory}
          canIdentity={data.canViewIdentity}
          onDone={() => setAdding(false)}
        />
      )}
      {editing && data && (
        <EditForm
          document={editing}
          subject={subject}
          canIdentity={data.canViewIdentity}
          queryKey={key}
          onDone={() => setEditingId(null)}
        />
      )}
      {data && data.items.length === 0 && !adding && <Empty>No files yet.</Empty>}
      {photos.length > 0 && (
        <ul className="mb-4 grid grid-cols-2 gap-3 sm:grid-cols-4">
          {photos.map((d) => (
            <li key={d.id} className="min-w-0">
              <a href={documentUrl(d.id)} target="_blank" rel="noreferrer" className="block">
                <img
                  src={documentUrl(d.id)}
                  alt={d.title}
                  loading="lazy"
                  className="aspect-square w-full rounded-md bg-slate-100 object-cover ring-1 ring-slate-200"
                />
              </a>
              <DocumentRow document={d} list={data!} queryKey={key} onEdit={() => setEditingId(d.id)} compact />
            </li>
          ))}
        </ul>
      )}
      {files.length > 0 && (
        <ul className="-mx-5 divide-y divide-slate-100 border-t border-slate-100">
          {files.map((d) => (
            <li key={d.id} className="px-5 py-3">
              <DocumentRow document={d} list={data!} queryKey={key} onEdit={() => setEditingId(d.id)} />
            </li>
          ))}
        </ul>
      )}
    </Card>
  )
}

export function ExpiryBadge({ expiresOn }: { expiresOn: string | null }) {
  const state = expiryState(expiresOn)
  if (!state || !expiresOn) return null
  if (state === 'expired') return <Badge tone="red">Expired {formatDay(expiresOn)}</Badge>
  if (state === 'soon') return <Badge tone="amber">Expires {formatDay(expiresOn)}</Badge>
  return <Badge>Expires {formatDay(expiresOn)}</Badge>
}

function DocumentRow({
  document: d,
  list,
  queryKey,
  onEdit,
  compact = false,
}: {
  document: DocumentItem
  list: DocumentList
  queryKey: string[]
  onEdit: () => void
  compact?: boolean
}) {
  const queryClient = useQueryClient()
  const remove = useMutation({
    mutationFn: () => del(`/api/v1/documents/${d.id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey }),
  })
  const shared = [d.sharedWithOwner && 'owner', d.sharedWithTenant && 'tenant'].filter(Boolean).join(' and ')
  const meta = [
    !compact && enumLabel(d.category),
    !compact && formatSize(d.sizeBytes),
    d.source === 'TenantPortal' ? 'from the tenant portal' : d.uploadedBy,
    formatDate(d.createdAtUtc),
  ].filter(Boolean)

  return (
    <div className={compact ? 'mt-1 text-xs' : 'flex flex-wrap items-start justify-between gap-x-4 gap-y-2'}>
      <div className="min-w-0">
        <p className={`${compact ? 'truncate' : ''} font-medium text-slate-900 ${compact ? 'text-xs' : 'text-sm'}`}>
          {compact ? (
            d.title
          ) : (
            <a href={documentUrl(d.id)} target="_blank" rel="noreferrer" className="hover:underline">
              {d.title}
            </a>
          )}
        </p>
        <p className="text-xs text-slate-500">{meta.join(' · ')}</p>
        <div className="mt-1 flex flex-wrap gap-1.5">
          <ExpiryBadge expiresOn={d.expiresOn} />
          {shared && <Badge tone="green">Shared with {shared}</Badge>}
        </div>
      </div>
      <div className={`flex flex-wrap items-center gap-1 ${compact ? 'mt-1' : ''}`}>
        <a href={documentUrl(d.id, true)} className="rounded-md px-2 py-1 text-xs font-medium text-brand-700 hover:bg-slate-100">
          Download
        </a>
        {list.canEdit && (
          <button onClick={onEdit} className="rounded-md px-2 py-1 text-xs font-medium text-slate-600 hover:bg-slate-100">
            Edit
          </button>
        )}
        {list.canDelete && (
          <button
            onClick={() => {
              if (window.confirm(`Delete "${d.title}"? The file is removed for good.`)) remove.mutate()
            }}
            disabled={remove.isPending}
            className="rounded-md px-2 py-1 text-xs font-medium text-red-700 hover:bg-red-50"
          >
            Delete
          </button>
        )}
      </div>
      <ErrorBox error={remove.error} />
    </div>
  )
}

function CategorySelect({
  value,
  onChange,
  canIdentity,
}: {
  value: DocumentCategory
  onChange: (v: DocumentCategory) => void
  canIdentity: boolean
}) {
  return (
    <SelectField label="Type" value={value} onChange={(e) => onChange(e.target.value as DocumentCategory)}>
      {DocumentCategories.filter((c) => canIdentity || c !== 'Identity' || value === 'Identity').map((c) => (
        <option key={c} value={c}>
          {c === 'Identity' ? 'Identity (ID card, passport)' : enumLabel(c)}
        </option>
      ))}
    </SelectField>
  )
}

function ShareBoxes({
  subject,
  owner,
  tenant,
  onOwner,
  onTenant,
}: {
  subject: DocumentSubject
  owner: boolean
  tenant: boolean
  onOwner: (v: boolean) => void
  onTenant: (v: boolean) => void
}) {
  const offer = shareable[subject]
  if (!offer.owner && !offer.tenant) return null
  return (
    <fieldset className="space-y-1">
      <legend className="text-sm font-medium text-slate-700">Show in portal</legend>
      {offer.owner && <Check label="Share with the owner" checked={owner} onChange={onOwner} />}
      {offer.tenant && <Check label="Share with the tenant" checked={tenant} onChange={onTenant} />}
    </fieldset>
  )
}

function Check({ label, checked, onChange }: { label: string; checked: boolean; onChange: (v: boolean) => void }) {
  return (
    <label className="flex items-center gap-2 text-sm text-slate-700">
      <input
        type="checkbox"
        className="rounded border-slate-300 text-brand-600"
        checked={checked}
        onChange={(e) => onChange(e.target.checked)}
      />
      {label}
    </label>
  )
}

function FormBox({ children, onSubmit }: { children: ReactNode; onSubmit: () => void }) {
  return (
    <form
      className="mb-4 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200"
      onSubmit={(e) => {
        e.preventDefault()
        onSubmit()
      }}
      noValidate
    >
      {children}
    </form>
  )
}

function UploadForm({
  subject,
  subjectId,
  defaultCategory,
  canIdentity,
  onDone,
}: {
  subject: DocumentSubject
  subjectId: string
  defaultCategory: DocumentCategory
  canIdentity: boolean
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const [file, setFile] = useState<File | null>(null)
  const [title, setTitle] = useState('')
  const [category, setCategory] = useState<DocumentCategory>(defaultCategory)
  const [expiresOn, setExpiresOn] = useState('')
  const [owner, setOwner] = useState(subject === 'Maintenance')
  const [tenant, setTenant] = useState(subject === 'Maintenance')
  const [problem, setProblem] = useState<string | null>(null)
  const save = useMutation({
    mutationFn: () => {
      const form = new FormData()
      form.append('file', file!)
      form.append('subject', subject)
      form.append('subjectId', subjectId)
      form.append('category', category)
      if (title.trim()) form.append('title', title.trim())
      if (expiresOn) form.append('expiresOn', expiresOn)
      form.append('sharedWithOwner', String(owner && shareable[subject].owner))
      form.append('sharedWithTenant', String(tenant && shareable[subject].tenant))
      return upload<DocumentItem>('/api/v1/documents', form)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['documents'] })
      onDone()
    },
  })
  const submit = () => {
    if (!file) return setProblem('Choose a file.')
    if (file.size > maxUploadBytes) return setProblem('Files can be up to 10 MB.')
    setProblem(null)
    save.mutate()
  }
  return (
    <FormBox onSubmit={submit}>
      <ErrorBox error={save.error} />
      <div className="space-y-1">
        <label htmlFor={`file-${subjectId}`} className="block text-sm font-medium text-slate-700">
          File
        </label>
        <input
          id={`file-${subjectId}`}
          type="file"
          accept={acceptedFiles}
          onChange={(e) => {
            const chosen = e.target.files?.[0] ?? null
            setFile(chosen)
            if (chosen?.type.startsWith('image/') && category === 'Other') setCategory('Photo')
          }}
          className="block w-full text-sm text-slate-700 file:mr-3 file:rounded-md file:border-0 file:bg-white file:px-3 file:py-2 file:text-sm file:font-medium file:ring-1 file:ring-slate-300"
        />
        <p className={`text-xs ${problem ? 'text-red-600' : 'text-slate-500'}`}>
          {problem ?? 'PDF, photo (JPEG, PNG, WebP), Word or Excel, up to 10 MB.'}
        </p>
      </div>
      <div className="grid gap-4 sm:grid-cols-3">
        <TextField
          label="Title"
          placeholder={file ? file.name.replace(/\.[^.]+$/, '') : 'Certificate of occupancy'}
          value={title}
          onChange={(e) => setTitle(e.target.value)}
          maxLength={200}
        />
        <CategorySelect value={category} onChange={setCategory} canIdentity={canIdentity} />
        <TextField
          label="Expires on"
          type="date"
          hint="For insurance, permits and certificates."
          value={expiresOn}
          onChange={(e) => setExpiresOn(e.target.value)}
        />
      </div>
      <ShareBoxes subject={subject} owner={owner} tenant={tenant} onOwner={setOwner} onTenant={setTenant} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending}>
          {save.isPending ? 'Uploading…' : 'Upload'}
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </FormBox>
  )
}

function EditForm({
  document: d,
  subject,
  canIdentity,
  queryKey,
  onDone,
}: {
  document: DocumentItem
  subject: DocumentSubject
  canIdentity: boolean
  queryKey: string[]
  onDone: () => void
}) {
  const queryClient = useQueryClient()
  const [title, setTitle] = useState(d.title)
  const [category, setCategory] = useState<DocumentCategory>(d.category)
  const [expiresOn, setExpiresOn] = useState(d.expiresOn ?? '')
  const [owner, setOwner] = useState(d.sharedWithOwner)
  const [tenant, setTenant] = useState(d.sharedWithTenant)
  const save = useMutation({
    mutationFn: () =>
      put<DocumentItem>(`/api/v1/documents/${d.id}`, {
        title: title.trim(),
        category,
        expiresOn: expiresOn || null,
        sharedWithOwner: owner,
        sharedWithTenant: tenant,
      }),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey })
      onDone()
    },
  })
  return (
    <FormBox onSubmit={() => save.mutate()}>
      <ErrorBox error={save.error} />
      <div className="grid gap-4 sm:grid-cols-3">
        <TextField label="Title" value={title} onChange={(e) => setTitle(e.target.value)} maxLength={200} />
        <CategorySelect value={category} onChange={setCategory} canIdentity={canIdentity} />
        <TextField label="Expires on" type="date" value={expiresOn} onChange={(e) => setExpiresOn(e.target.value)} />
      </div>
      <ShareBoxes subject={subject} owner={owner} tenant={tenant} onOwner={setOwner} onTenant={setTenant} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending || !title.trim()}>
          Save
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </FormBox>
  )
}
