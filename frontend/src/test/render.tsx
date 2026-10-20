import type { ReactElement } from 'react'
import { render } from '@testing-library/react'
import { MemoryRouter } from 'react-router-dom'
import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { SessionProvider } from '../app/session'
import { App } from '../App'

type Handler = (url: string, init: RequestInit) => { status: number; body?: unknown }

/** Replaces fetch with a handler and records every request made. */
export function mockApi(handler: Handler) {
  const calls: { url: string; init: RequestInit }[] = []
  globalThis.fetch = vi.fn(async (input: RequestInfo | URL, init: RequestInit = {}) => {
    const url = input.toString()
    calls.push({ url, init })
    const { status, body } = handler(url, init)
    return new Response(body === undefined ? null : JSON.stringify(body), { status })
  }) as typeof fetch
  return calls
}

export function renderApp(path: string, ui: ReactElement = <App />) {
  const queryClient = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  return render(
    <QueryClientProvider client={queryClient}>
      <MemoryRouter initialEntries={[path]}>
        <SessionProvider>{ui}</SessionProvider>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

export const adminOrg = {
  id: 'org-1',
  name: 'Lekki Homes Ltd',
  slug: 'lekki-homes-ltd',
  type: 'ManagementCompany',
  status: 'Active',
  roles: ['OrganizationAdmin'],
  permissions: ['Organization.View', 'Settings.Manage', 'Branches.View', 'Branches.Manage', 'Users.View', 'Users.Manage', 'Audit.View'],
}

export const agentOrg = {
  id: 'org-2',
  name: 'XYZ Estate Services',
  slug: 'xyz',
  type: 'ManagementCompany',
  status: 'Active',
  roles: ['Agent'],
  permissions: ['Organization.View'],
}

export const me = (organizations: unknown[]) => ({
  userId: 'user-1',
  email: 'seun@example.com',
  fullName: 'Seun Adelaja',
  isPlatformAdmin: false,
  organizations,
})
