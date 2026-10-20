import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, patch, post } from '../../lib/api'
import type { Branch, CreatedInvitation, Invitation, Member } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions, StaffRoles, roleLabel } from '../../lib/permissions'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, SelectField, TextField } from '../../components/ui'
import { formatDate } from '../../lib/format'
import { SupportAccessCard } from '../support/SupportAccessCard'

export function TeamPage() {
  const { organization, can, me } = useSession()
  const canManage = can(Permissions.usersManage)
  const base = `/api/v1/organizations/${organization!.id}`
  const queryClient = useQueryClient()
  const members = useQuery({ queryKey: ['members', organization!.id], queryFn: () => get<Member[]>(`${base}/members`) })
  const invitations = useQuery({
    queryKey: ['invitations', organization!.id],
    queryFn: () => get<Invitation[]>(`${base}/invitations`),
    enabled: canManage,
  })
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => get<Branch[]>('/api/v1/branches'), enabled: can(Permissions.branchesView) })
  const branchName = (id: string) => branches.data?.find((b) => b.id === id)?.name ?? 'Unknown branch'
  const [inviting, setInviting] = useState(false)

  const changeRole = useMutation({
    mutationFn: ({ member, role }: { member: Member; role: string }) =>
      patch<Member>(`${base}/members/${member.membershipId}`, { roles: [role], branchIds: member.branchIds }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['members'] }),
  })
  const revoke = useMutation({
    mutationFn: (member: Member) => del(`${base}/members/${member.membershipId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['members'] }),
  })
  const cancelInvite = useMutation({
    mutationFn: (invitation: Invitation) => del(`${base}/invitations/${invitation.id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ['invitations'] }),
  })

  return (
    <>
      <PageHeader
        title="Team"
        description="Everyone who can sign in to this organization, and what they can do."
        actions={canManage && !inviting && <Button onClick={() => setInviting(true)}>Invite someone</Button>}
      />
      {inviting && <InviteForm base={base} branches={branches.data ?? []} onDone={() => setInviting(false)} />}

      <div className="space-y-6">
        <Card title="Members">
          <ErrorBox error={members.error ?? changeRole.error ?? revoke.error} />
          <ul className="divide-y divide-slate-100">
            {members.data?.map((m) => (
              <li key={m.membershipId} className="flex flex-wrap items-center justify-between gap-3 py-3">
                <div className="min-w-0">
                  <p className="text-sm font-medium">
                    {m.fullName} {m.userId === me?.userId && <span className="text-slate-500">(you)</span>}
                  </p>
                  <p className="truncate text-sm text-slate-600">{m.email}</p>
                  {m.branchIds.length > 0 && (
                    <p className="mt-1 text-xs text-slate-500">Branches: {m.branchIds.map(branchName).join(', ')}</p>
                  )}
                </div>
                <div className="flex items-center gap-2">
                  {canManage ? (
                    <>
                      <label className="sr-only" htmlFor={`role-${m.membershipId}`}>
                        Role for {m.fullName}
                      </label>
                      <select
                        id={`role-${m.membershipId}`}
                        className="rounded-md border-0 py-1.5 text-sm ring-1 ring-slate-300"
                        value={m.roles[0]}
                        onChange={(e) => changeRole.mutate({ member: m, role: e.target.value })}
                      >
                        {StaffRoles.map((r) => (
                          <option key={r.value} value={r.value}>
                            {r.label}
                          </option>
                        ))}
                      </select>
                      <Button
                        variant="danger"
                        onClick={() => {
                          if (confirm(`Remove ${m.fullName} from ${organization!.name}? They lose access immediately.`)) revoke.mutate(m)
                        }}
                      >
                        Remove
                      </Button>
                    </>
                  ) : (
                    m.roles.map((r) => <Badge key={r}>{roleLabel(r)}</Badge>)
                  )}
                </div>
              </li>
            ))}
          </ul>
        </Card>

        {canManage && (
          <Card title="Invitations">
            <ErrorBox error={invitations.error ?? cancelInvite.error} />
            {invitations.data?.length === 0 && <Empty>No invitations sent yet.</Empty>}
            <ul className="divide-y divide-slate-100">
              {invitations.data?.map((i) => (
                <li key={i.id} className="flex flex-wrap items-center justify-between gap-3 py-3">
                  <div>
                    <p className="text-sm font-medium">{i.email}</p>
                    <p className="text-xs text-slate-500">
                      {roleLabel(i.role)} · sent {formatDate(i.createdAtUtc)}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge tone={i.status === 'Pending' ? 'amber' : i.status === 'Accepted' ? 'green' : 'slate'}>{i.status}</Badge>
                    {i.status === 'Pending' && (
                      <Button variant="ghost" onClick={() => cancelInvite.mutate(i)}>
                        Cancel
                      </Button>
                    )}
                  </div>
                </li>
              ))}
            </ul>
          </Card>
        )}

        {canManage && <SupportAccessCard />}
      </div>
    </>
  )
}

const inviteSchema = z.object({
  email: z.email('Enter a valid email address.'),
  role: z.string().min(1),
  branchIds: z.array(z.string()),
})
type InviteValues = z.infer<typeof inviteSchema>

function InviteForm({ base, branches, onDone }: { base: string; branches: Branch[]; onDone: () => void }) {
  const queryClient = useQueryClient()
  const [created, setCreated] = useState<CreatedInvitation | null>(null)
  const { register, handleSubmit, formState } = useForm<InviteValues>({
    resolver: zodResolver(inviteSchema),
    defaultValues: { email: '', role: 'PropertyManager', branchIds: [] },
  })
  const invite = useMutation({
    mutationFn: (v: InviteValues) => post<CreatedInvitation>(`${base}/invitations`, v),
    onSuccess: async (result) => {
      setCreated(result)
      await queryClient.invalidateQueries({ queryKey: ['invitations'] })
    },
  })

  if (created) {
    const link = `${window.location.origin}/join?token=${encodeURIComponent(created.acceptToken)}&email=${encodeURIComponent(created.invitation.email)}`
    return (
      <div className="mb-6">
        <Card title="Invitation created">
          <p className="text-sm text-slate-700">
            Send this link to {created.invitation.email}. It works once and expires in 7 days. It won't be shown again.
          </p>
          <div className="mt-3 flex gap-2">
            <input readOnly value={link} aria-label="Invitation link" className="min-w-0 flex-1 rounded-md bg-slate-50 px-3 py-2 font-mono text-xs ring-1 ring-slate-200" />
            <Button variant="secondary" onClick={() => navigator.clipboard?.writeText(link)}>
              Copy
            </Button>
          </div>
          <div className="mt-4">
            <Button onClick={onDone}>Done</Button>
          </div>
        </Card>
      </div>
    )
  }

  return (
    <div className="mb-6">
      <Card title="Invite someone">
        <form className="space-y-4" onSubmit={handleSubmit((v) => invite.mutate(v))} noValidate>
          <ErrorBox error={invite.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Email" type="email" {...register('email')} error={formState.errors.email?.message} />
            <SelectField label="Role" {...register('role')}>
              {StaffRoles.map((r) => (
                <option key={r.value} value={r.value}>
                  {r.label}
                </option>
              ))}
            </SelectField>
          </div>
          {branches.length > 0 && (
            <fieldset>
              <legend className="text-sm font-medium text-slate-700">Limit to branches (optional)</legend>
              <p className="text-xs text-slate-500">Leave all unticked for access to the whole organization.</p>
              <div className="mt-2 flex flex-wrap gap-4">
                {branches.map((b) => (
                  <label key={b.id} className="flex items-center gap-2 text-sm">
                    <input type="checkbox" value={b.id} {...register('branchIds')} /> {b.name}
                  </label>
                ))}
              </div>
            </fieldset>
          )}
          <div className="flex gap-2">
            <Button type="submit" disabled={invite.isPending}>
              Create invitation
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
