import { useMutation } from '@tanstack/react-query'
import { Navigate, useNavigate, useSearchParams } from 'react-router-dom'
import { post } from '../../lib/api'
import type { OrganizationSummary } from '../../lib/types'
import { useSession } from '../../app/session'
import { Button, ErrorBox } from '../../components/ui'
import { AuthLayout } from '../auth/AuthLayout'

/** Landing page for an invitation link. Signed-out people create a login; signed-in people accept directly. */
export function JoinPage() {
  const [params] = useSearchParams()
  const token = params.get('token') ?? ''
  const email = params.get('email') ?? ''
  const { me, isLoading, refresh, selectOrganization } = useSession()
  const navigate = useNavigate()
  const accept = useMutation({
    mutationFn: () => post<OrganizationSummary>('/api/v1/invitations/accept', { token }),
    onSuccess: async (org) => {
      await refresh()
      selectOrganization(org.id)
      navigate('/app', { replace: true })
    },
  })

  if (isLoading) return null
  if (!token) return <Navigate to="/login" replace />
  if (!me) {
    return (
      <AuthLayout title="You've been invited" subtitle="Create a login, or sign in if you already have one.">
        <div className="space-y-3">
          <Button
            className="w-full"
            onClick={() => navigate(`/register?invite=${encodeURIComponent(token)}&email=${encodeURIComponent(email)}`)}
          >
            Create a login
          </Button>
          <Button
            variant="secondary"
            className="w-full"
            onClick={() => navigate('/login', { state: { from: `/join?token=${encodeURIComponent(token)}` } })}
          >
            I already have a login
          </Button>
        </div>
      </AuthLayout>
    )
  }

  return (
    <AuthLayout title="Accept invitation" subtitle={`Signed in as ${me.email}.`}>
      <div className="space-y-4">
        <ErrorBox error={accept.error} />
        <Button className="w-full" onClick={() => accept.mutate()} disabled={accept.isPending}>
          Join organization
        </Button>
      </div>
    </AuthLayout>
  )
}
