import { useRef } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { documentUrl, get, upload } from '../../lib/api'
import type { PortalDocument } from '../../lib/types'
import { enumLabel, formatDate } from '../../lib/format'
import { Button, Card, ErrorBox } from '../../components/ui'
import { ExpiryBadge } from '../documents/DocumentsPanel'
import { acceptedPhotos, formatSize, maxUploadBytes } from '../documents/documentFormat'

type Role = 'owner' | 'tenant'

function usePortalDocuments(role: Role) {
  return useQuery({
    queryKey: ['portal', role, 'documents'],
    queryFn: () => get<PortalDocument[]>(`/api/v1/portal/${role}/documents`),
  })
}

/** Documents shared with the owner or tenant, other than repair photos (those show on the repair). */
export function PortalDocumentsCard({ role }: { role: Role }) {
  const documents = usePortalDocuments(role)
  const items = documents.data?.filter((d) => d.subject !== 'Maintenance') ?? []
  if (!documents.error && items.length === 0) return null
  return (
    <Card title="Documents">
      <ErrorBox error={documents.error} />
      <ul className="-mx-5 -my-2 divide-y divide-slate-100">
        {items.map((d) => (
          <li key={d.id} className="flex flex-wrap items-center justify-between gap-2 px-5 py-3 text-sm">
            <span className="min-w-0">
              <a href={documentUrl(d.id)} target="_blank" rel="noreferrer" className="font-medium text-slate-900 hover:underline">
                {d.title}
              </a>
              <span className="block truncate text-slate-500">
                {enumLabel(d.category)} · {d.subjectLabel} · {formatSize(d.sizeBytes)}
              </span>
            </span>
            <span className="flex items-center gap-2">
              <ExpiryBadge expiresOn={d.expiresOn} />
              <a href={documentUrl(d.id, true)} className="rounded-md px-2 py-1 text-xs font-medium text-brand-700 hover:bg-slate-100">
                Download
              </a>
            </span>
          </li>
        ))}
      </ul>
    </Card>
  )
}

/** Photos and files on one repair. A tenant can add photos while the repair is open. */
export function RequestFiles({ role, requestId, canAddPhotos }: { role: Role; requestId: string; canAddPhotos: boolean }) {
  const queryClient = useQueryClient()
  const input = useRef<HTMLInputElement>(null)
  const documents = usePortalDocuments(role)
  const items = documents.data?.filter((d) => d.subject === 'Maintenance' && d.subjectId === requestId) ?? []
  const add = useMutation({
    mutationFn: (file: File) => {
      if (file.size > maxUploadBytes) throw new Error('Photos can be up to 10 MB.')
      const form = new FormData()
      form.append('file', file)
      return upload<PortalDocument>(`/api/v1/portal/tenant/maintenance/${requestId}/photos`, form)
    },
    onSuccess: () =>
      queryClient.invalidateQueries({
        queryKey: ['portal', role, 'documents'],
      }),
    onSettled: () => {
      if (input.current) input.current.value = ''
    },
  })
  if (items.length === 0 && !canAddPhotos) return null
  return (
    <Card
      title="Photos"
      actions={
        canAddPhotos && (
          <>
            <input
              ref={input}
              type="file"
              accept={acceptedPhotos}
              className="sr-only"
              id={`photo-${requestId}`}
              aria-label="Choose a photo"
              onChange={(e) => {
                const file = e.target.files?.[0]
                if (file) add.mutate(file)
              }}
            />
            <Button variant="secondary" disabled={add.isPending} onClick={() => input.current?.click()}>
              {add.isPending ? 'Uploading…' : 'Add a photo'}
            </Button>
          </>
        )
      }
    >
      <ErrorBox error={add.error ?? documents.error} />
      {items.length === 0 && <p className="text-sm text-slate-500">A photo helps us see the problem before we visit.</p>}
      <ul className="grid grid-cols-2 gap-3 sm:grid-cols-4">
        {items.map((d) => (
          <li key={d.id} className="min-w-0">
            {d.contentType.startsWith('image/') ? (
              <a href={documentUrl(d.id)} target="_blank" rel="noreferrer">
                <img
                  src={documentUrl(d.id)}
                  alt={d.title}
                  loading="lazy"
                  className="aspect-square w-full rounded-md bg-slate-100 object-cover ring-1 ring-slate-200"
                />
              </a>
            ) : (
              <a
                href={documentUrl(d.id)}
                target="_blank"
                rel="noreferrer"
                className="block rounded-md bg-slate-50 p-3 text-sm font-medium text-brand-700 ring-1 ring-slate-200 hover:underline"
              >
                {d.title}
              </a>
            )}
            <p className="mt-1 truncate text-xs text-slate-500">
              {d.byYou ? 'Added by you' : d.title} · {formatDate(d.createdAtUtc)}
            </p>
          </li>
        ))}
      </ul>
    </Card>
  )
}
