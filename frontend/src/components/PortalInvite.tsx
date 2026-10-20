import { useState } from 'react'
import { useMutation, useQueryClient } from '@tanstack/react-query'
import { post } from '../lib/api'
import type { CreatedInvitation } from '../lib/types'
import { Button, ErrorBox } from './ui'

/** Invites an owner or tenant to their portal and shows the one-time join link, like a team invitation. */
export function PortalInvite({ path, invalidate, name }: { path: string; invalidate: unknown[]; name: string }) {
  const queryClient = useQueryClient()
  const [created, setCreated] = useState<CreatedInvitation | null>(null)
  const invite = useMutation({
    mutationFn: () => post<CreatedInvitation>(path),
    onSuccess: async (result) => {
      setCreated(result)
      await queryClient.invalidateQueries({ queryKey: invalidate })
    },
  })

  if (created) {
    const link = `${window.location.origin}/join?token=${encodeURIComponent(created.acceptToken)}&email=${encodeURIComponent(created.invitation.email)}`
    return (
      <div>
        <p className="text-sm font-medium text-slate-800">Portal invitation created</p>
        <p className="mt-1 text-sm text-slate-700">
          Send this link to {name} at {created.invitation.email}. It works once and expires in 7 days. It won't be shown again.
        </p>
        <div className="mt-3 flex gap-2">
          <input
            readOnly
            value={link}
            aria-label="Invitation link"
            className="min-w-0 flex-1 rounded-md bg-slate-50 px-3 py-2 font-mono text-xs ring-1 ring-slate-200"
          />
          <Button variant="secondary" onClick={() => navigator.clipboard?.writeText(link)}>
            Copy
          </Button>
        </div>
        <div className="mt-4">
          <Button onClick={() => setCreated(null)}>Done</Button>
        </div>
      </div>
    )
  }

  return (
    <div className="space-y-3">
      <p className="text-sm text-slate-600">{name} doesn't have portal access yet. The invitation goes to their email address.</p>
      <ErrorBox error={invite.error} />
      <Button variant="secondary" disabled={invite.isPending} onClick={() => invite.mutate()}>
        Invite to portal
      </Button>
    </div>
  )
}
