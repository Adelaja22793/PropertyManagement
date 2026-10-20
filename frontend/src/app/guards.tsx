import type { ReactNode } from 'react'
import { Navigate, useLocation } from 'react-router-dom'
import { useSession } from './session'

function Loading() {
  return <div className="flex min-h-full items-center justify-center text-sm text-slate-500">Loading…</div>
}

/** Pages that need a signed-in user with at least one organization. */
export function RequireSession({ children }: { children: ReactNode }) {
  const { me, isLoading, organization } = useSession()
  const location = useLocation()
  if (isLoading) return <Loading />
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  if (!organization) return <Navigate to="/organizations/new" replace />
  return <>{children}</>
}

/** Pages that need a signed-in user, with or without an organization. */
export function RequireUser({ children }: { children: ReactNode }) {
  const { me, isLoading } = useSession()
  const location = useLocation()
  if (isLoading) return <Loading />
  if (!me) return <Navigate to="/login" replace state={{ from: location.pathname + location.search }} />
  return <>{children}</>
}

/** Sign-in pages: a signed-in user goes straight to the app. */
export function GuestOnly({ children }: { children: ReactNode }) {
  const { me, isLoading } = useSession()
  if (isLoading) return <Loading />
  if (me) return <Navigate to="/app" replace />
  return <>{children}</>
}

export function Forbidden() {
  return (
    <div className="rounded-lg bg-white p-8 text-center ring-1 ring-slate-200">
      <h1 className="text-lg font-semibold">You don't have access to this page</h1>
      <p className="mt-2 text-sm text-slate-600">Ask an organization admin if you need it.</p>
    </div>
  )
}

/** Shows the page when the member holds the permission, or any one of a list of permissions. */
export function RequirePermission({ permission, children }: { permission: string | string[]; children: ReactNode }) {
  const { can } = useSession()
  const allowed = Array.isArray(permission) ? permission.some(can) : can(permission)
  return allowed ? <>{children}</> : <Forbidden />
}
