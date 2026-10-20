import { useState, type FormEvent } from 'react'
import { Link, useNavigate } from 'react-router-dom'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post } from '../../lib/api'
import type { SupportGrant } from '../../lib/types'
import { useSession } from '../../app/session'
import { Forbidden } from '../../app/guards'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, SelectField, TextAreaField, TextField } from '../../components/ui'
import { grantTiming, statusTone } from './supportGrant'

const hourOptions = [1, 2, 4, 8, 24]

/** Platform support asks a company for read-only access and opens it once approved (design 4.7). */
export function SupportDeskPage() {
  const { me } = useSession()
  if (!me?.isPlatformAdmin) return <Forbidden />
  return (
    <>
      <PageHeader
        title="Support desk"
        description="Ask a company for read-only access to help them. Their admin decides, and can end it at any time."
      />
      {me.twoFactorEnabled ? (
        <div className="space-y-6">
          <RequestForm />
          <MyGrants />
        </div>
      ) : (
        <Card>
          <p className="text-sm text-slate-700">
            Turn on two-step sign-in before asking for access.{' '}
            <Link to="/app/security" className="font-medium text-brand-700 underline">
              Set it up
            </Link>
          </p>
        </Card>
      )}
    </>
  )
}

function RequestForm() {
  const queryClient = useQueryClient()
  const [organization, setOrganization] = useState('')
  const [reason, setReason] = useState('')
  const [hours, setHours] = useState('2')
  const request = useMutation({
    mutationFn: () => post<SupportGrant>('/api/v1/platform/support-grants', { organization, reason, hours: Number(hours) }),
    onSuccess: async () => {
      setOrganization('')
      setReason('')
      await queryClient.invalidateQueries({ queryKey: ['my-support-grants'] })
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    request.mutate()
  }

  return (
    <Card title="Ask for access">
      <form onSubmit={submit} className="space-y-4">
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField
            label="Company's web address name"
            hint="As in their public page, /to-let/ikoyi-homes"
            value={organization}
            onChange={(e) => setOrganization(e.target.value)}
            required
          />
          <SelectField label="For how long" value={hours} onChange={(e) => setHours(e.target.value)}>
            {hourOptions.map((h) => (
              <option key={h} value={h}>
                {h} {h === 1 ? 'hour' : 'hours'}
              </option>
            ))}
          </SelectField>
        </div>
        <TextAreaField
          label="Why you need it"
          hint="The company's admin sees this. Include the ticket number."
          value={reason}
          onChange={(e) => setReason(e.target.value)}
          maxLength={500}
          required
        />
        <ErrorBox error={request.error} />
        {request.isSuccess && <p className="text-sm text-green-700">Sent. Their admins have been emailed.</p>}
        <Button type="submit" disabled={request.isPending || !organization.trim() || !reason.trim()}>
          {request.isPending ? 'Sending…' : 'Send request'}
        </Button>
      </form>
    </Card>
  )
}

function MyGrants() {
  const { refresh, selectOrganization } = useSession()
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const grants = useQuery({ queryKey: ['my-support-grants'], queryFn: () => get<SupportGrant[]>('/api/v1/platform/support-grants') })
  const end = useMutation({
    mutationFn: (id: string) => post<SupportGrant>(`/api/v1/platform/support-grants/${id}/end`),
    onSuccess: async () => {
      await Promise.all([queryClient.invalidateQueries({ queryKey: ['my-support-grants'] }), refresh()])
    },
  })
  const open = useMutation({
    mutationFn: async (g: SupportGrant) => {
      await refresh()
      selectOrganization(g.organizationId)
      navigate('/app')
    },
  })

  return (
    <Card title="Your requests">
      <ErrorBox error={grants.error ?? end.error ?? open.error} />
      {grants.data?.length === 0 && <Empty>No requests yet.</Empty>}
      <ul className="divide-y divide-slate-100">
        {grants.data?.map((g) => (
          <li key={g.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
            <div className="min-w-0">
              <p className="text-sm font-medium">{g.organizationName}</p>
              <p className="text-sm text-slate-700">{g.reason}</p>
              <p className="text-xs text-slate-500">{grantTiming(g)}</p>
            </div>
            <div className="flex items-center gap-2">
              <Badge tone={statusTone[g.status]}>{g.status}</Badge>
              {g.status === 'Active' && (
                <Button onClick={() => open.mutate(g)} disabled={open.isPending}>
                  Open
                </Button>
              )}
              {(g.status === 'Active' || g.status === 'Pending') && (
                <Button variant="ghost" onClick={() => end.mutate(g.id)} disabled={end.isPending}>
                  {g.status === 'Active' ? 'End now' : 'Withdraw'}
                </Button>
              )}
            </div>
          </li>
        ))}
      </ul>
    </Card>
  )
}
