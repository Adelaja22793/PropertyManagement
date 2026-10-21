import { Suspense, useEffect, useState } from 'react'
import { Link, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useQueryClient } from '@tanstack/react-query'
import { useSession } from './session'
import { ApiError } from '../lib/api'
import { formatDate, formatDay } from '../lib/format'
import { isOverdue, twoFactorDeadline } from '../features/account/twoFactor'
import { Permissions, PortalRoles } from '../lib/permissions'
import { PortalHome } from '../features/portal/PortalHome'

const nav = [
  { to: '/app', label: 'Dashboard', end: true, permission: Permissions.organizationView },
  { to: '/app/properties', label: 'Properties', permission: Permissions.propertiesView },
  { to: '/app/owners', label: 'Owners', permission: Permissions.ownersView },
  { to: '/app/tenants', label: 'Tenants', permission: Permissions.tenantsView },
  { to: '/app/leases', label: 'Leases', permission: Permissions.leasesView },
  { to: '/app/maintenance', label: 'Maintenance', permission: [Permissions.maintenanceView, Permissions.maintenanceUpdateAssigned] },
  { to: '/app/listings', label: 'Listings', permission: [Permissions.listingsManage, Permissions.leadsManage] },
  {
    to: '/app/reports',
    label: 'Reports',
    permission: [Permissions.propertiesView, Permissions.leasesView, Permissions.maintenanceView, Permissions.leadsManage, Permissions.auditView],
  },
  { to: '/app/branches', label: 'Branches', permission: Permissions.branchesView },
  { to: '/app/team', label: 'Team', permission: Permissions.usersView },
  { to: '/app/roles', label: 'Roles', permission: Permissions.usersView },
  { to: '/app/audit', label: 'Audit log', permission: Permissions.auditView },
  { to: '/app/settings', label: 'Settings', permission: Permissions.organizationView },
]

export function AppLayout() {
  const { me, organization, can, selectOrganization, signOut } = useSession()
  const navigate = useNavigate()
  const { pathname } = useLocation()
  const [menuOpen, setMenuOpen] = useState(false)
  const blockedByServer = useTwoFactorBlock()
  if (!me || !organization) return null

  // Roles that need two-step sign-in get a grace period, then only the security page stays open.
  const deadline = twoFactorDeadline(me, organization)
  const onSecurityPage = pathname === '/app/security'
  const shutOut = !onSecurityPage && deadline !== null && (isOverdue(deadline) || blockedByServer)

  // Owners and tenants invited to a portal have no staff permissions; they never see staff pages.
  const portalOnly = organization.roles.length > 0 && organization.roles.every((r) => (PortalRoles as readonly string[]).includes(r))
  // Staff who are also an owner or tenant reach their own portal from the menu.
  const hasPortal = organization.roles.some((r) => (PortalRoles as readonly string[]).includes(r))
  const links = portalOnly
    ? [{ to: '/app', label: 'Home', end: true }]
    : [
        ...nav.filter((item) => (Array.isArray(item.permission) ? item.permission.some(can) : can(item.permission))),
        ...(hasPortal ? [{ to: '/app/portal', label: 'My portal', end: false }] : []),
      ]

  return (
    <div className="flex min-h-full flex-col md:flex-row">
      <aside className="border-b border-slate-200 bg-white md:w-60 md:shrink-0 md:border-r md:border-b-0">
        <div className="flex items-center justify-between px-4 py-4">
          <div className="flex items-center gap-2">
            <div className="flex h-8 w-8 items-center justify-center rounded-lg bg-brand-600 text-sm font-bold text-white">P</div>
            <span className="text-sm font-semibold">Property Management</span>
          </div>
          <button
            className="rounded-md p-2 text-slate-600 hover:bg-slate-100 md:hidden"
            aria-label="Menu"
            aria-expanded={menuOpen}
            onClick={() => setMenuOpen((o) => !o)}
          >
            ☰
          </button>
        </div>
        <nav aria-label="Main" className={`${menuOpen ? 'block' : 'hidden'} space-y-1 px-3 pb-4 md:block`}>
          {links.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              onClick={() => setMenuOpen(false)}
              className={({ isActive }) =>
                `block rounded-md px-3 py-2 text-sm font-medium ${isActive ? 'bg-brand-50 text-brand-700' : 'text-slate-700 hover:bg-slate-100'}`
              }
            >
              {item.label}
            </NavLink>
          ))}
          {!portalOnly && <p className="px-3 pt-4 text-xs text-slate-400">Rent, payments and payouts arrive once the finance design is signed off.</p>}
        </nav>
      </aside>

      <div className="flex min-w-0 flex-1 flex-col">
        <header className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-200 bg-white px-4 py-3 sm:px-6">
          <div className="flex items-center gap-2">
            <label htmlFor="org-switcher" className="sr-only">
              Organization
            </label>
            <select
              id="org-switcher"
              value={organization.id}
              onChange={(e) => {
                if (e.target.value === '__new') navigate('/organizations/new')
                else selectOrganization(e.target.value)
              }}
              className="max-w-[16rem] rounded-md border-0 py-1.5 pr-8 pl-3 text-sm font-medium ring-1 ring-slate-300 focus:ring-2 focus:ring-brand-500"
            >
              {me.organizations.map((o) => (
                <option key={o.id} value={o.id}>
                  {o.name}
                  {o.supportAccessUntilUtc ? ' (support)' : ''}
                </option>
              ))}
              <option value="__new">+ New organization…</option>
            </select>
            {organization.status !== 'Active' && (
              <span className="rounded-full bg-amber-50 px-2 py-0.5 text-xs font-medium text-amber-800">{organization.status}</span>
            )}
          </div>
          <div className="flex items-center gap-3 text-sm">
            <span className="hidden text-slate-600 sm:inline">{me.fullName}</span>
{me.isPlatformAdmin && (
              <Link to="/app/support-desk" className="rounded-md px-2 py-1 font-medium text-slate-600 hover:bg-slate-100">
                Support desk
              </Link>
            )}
                        <Link to="/app/security" className="rounded-md px-2 py-1 font-medium text-slate-600 hover:bg-slate-100">
              Security
            </Link>
            <button
              className="rounded-md px-2 py-1 font-medium text-slate-600 hover:bg-slate-100"
              onClick={async () => {
                await signOut()
                navigate('/login', { replace: true })
              }}
            >
              Sign out
            </button>
          </div>
        </header>
        {organization.supportAccessUntilUtc && (
          <div role="status" className="border-b border-sky-200 bg-sky-50 px-4 py-2.5 text-sm text-sky-900 sm:px-6">
            Support access to {organization.name}: read-only, ends {formatDate(organization.supportAccessUntilUtc)}. Everything you open is
            in their audit log.
          </div>
        )}
        {deadline && !shutOut && !onSecurityPage && !isOverdue(deadline) && (
          <div role="status" className="border-b border-amber-200 bg-amber-50 px-4 py-2.5 text-sm text-amber-900 sm:px-6">
            Your role needs two-step sign-in. Turn it on by {formatDay(deadline.toISOString().slice(0, 10))} to keep using {organization.name}.{' '}
            <Link to="/app/security" className="font-medium underline">
              Turn it on
            </Link>
          </div>
        )}
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:px-6 sm:py-8">
          {shutOut ? (
            <div className="mx-auto max-w-lg rounded-lg bg-white p-6 shadow-sm ring-1 ring-slate-200">
              <h1 className="text-lg font-semibold text-slate-900">Turn on two-step sign-in</h1>
              <p className="mt-2 text-sm text-slate-600">
                Your role in {organization.name} needs a code from an authenticator app when you sign in. Turn it on to keep using this organization.
              </p>
              <Link
                to="/app/security"
                className="mt-4 inline-flex rounded-md bg-brand-600 px-3.5 py-2 text-sm font-medium text-white hover:bg-brand-700"
              >
                Set it up now
              </Link>
            </div>
          ) : portalOnly && !onSecurityPage ? (
            <PortalHome key={organization.id} />
          ) : (
            <Suspense fallback={<p className="text-sm text-slate-500">Loading…</p>}>
              <Outlet key={organization.id} />
            </Suspense>
          )}
        </main>
      </div>
    </div>
  )
}

/** True once any request has been refused because two-step sign-in is overdue (the server's clock decides). */
function useTwoFactorBlock() {
  const queryClient = useQueryClient()
  const [blocked, setBlocked] = useState(false)
  useEffect(() => {
    const isBlock = (error: unknown) => error instanceof ApiError && error.code === 'two_factor_required' && error.status === 403
    const unsubscribe = queryClient.getQueryCache().subscribe((event) => {
      if (event.type === 'updated' && isBlock(event.query.state.error)) setBlocked(true)
    })
    return unsubscribe
  }, [queryClient])
  return blocked
}
