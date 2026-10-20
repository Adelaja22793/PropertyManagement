import { useState } from 'react'
import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useSession } from './session'
import { Permissions } from '../lib/permissions'

const nav = [
  { to: '/app', label: 'Dashboard', end: true, permission: Permissions.organizationView },
  { to: '/app/branches', label: 'Branches', permission: Permissions.branchesView },
  { to: '/app/team', label: 'Team', permission: Permissions.usersView },
  { to: '/app/audit', label: 'Audit log', permission: Permissions.auditView },
  { to: '/app/settings', label: 'Settings', permission: Permissions.organizationView },
]

export function AppLayout() {
  const { me, organization, can, selectOrganization, signOut } = useSession()
  const navigate = useNavigate()
  const [menuOpen, setMenuOpen] = useState(false)
  if (!me || !organization) return null

  const links = nav.filter((item) => can(item.permission))

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
          <p className="px-3 pt-4 text-xs text-slate-400">Properties, tenants, leases and rent arrive in the next phases.</p>
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
        <main className="mx-auto w-full max-w-5xl flex-1 px-4 py-6 sm:px-6 sm:py-8">
          <Outlet key={organization.id} />
        </main>
      </div>
    </div>
  )
}
