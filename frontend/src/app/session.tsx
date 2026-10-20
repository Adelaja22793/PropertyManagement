import { createContext, useCallback, useContext, useEffect, useMemo, useState, type ReactNode } from 'react'
import { useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, get, organizationStore, post } from '../lib/api'
import type { Me, OrganizationSummary } from '../lib/types'

interface Session {
  me: Me | null
  isLoading: boolean
  organization: OrganizationSummary | null
  can: (permission: string) => boolean
  selectOrganization: (id: string) => void
  signedIn: (me: Me) => void
  signOut: () => Promise<void>
  refresh: () => Promise<void>
}

const SessionContext = createContext<Session | null>(null)

async function fetchMe(): Promise<Me | null> {
  try {
    return await get<Me>('/api/v1/auth/me')
  } catch (error) {
    if (error instanceof ApiError && error.status === 401) return null
    throw error
  }
}

export function SessionProvider({ children }: { children: ReactNode }) {
  const queryClient = useQueryClient()
  const { data: me = null, isLoading } = useQuery({ queryKey: ['me'], queryFn: fetchMe, staleTime: 60_000 })
  const [selectedId, setSelectedId] = useState<string | null>(() => organizationStore.get())

  // Fall back to the first organization when the remembered one is no longer available.
  const organization = useMemo(() => {
    if (!me) return null
    return me.organizations.find((o) => o.id === selectedId) ?? me.organizations[0] ?? null
  }, [me, selectedId])

  useEffect(() => {
    if (organization && organizationStore.get() !== organization.id) organizationStore.set(organization.id)
  }, [organization])

  const selectOrganization = useCallback(
    (id: string) => {
      organizationStore.set(id)
      setSelectedId(id)
      // Everything cached belongs to the previous organization.
      queryClient.removeQueries({ predicate: (q) => q.queryKey[0] !== 'me' })
    },
    [queryClient],
  )

  const signedIn = useCallback(
    (next: Me) => {
      queryClient.setQueryData(['me'], next)
      const keep = next.organizations.find((o) => o.id === organizationStore.get())
      selectOrganization(keep?.id ?? next.organizations[0]?.id ?? '')
    },
    [queryClient, selectOrganization],
  )

  const signOut = useCallback(async () => {
    await post('/api/v1/auth/logout')
    organizationStore.set(null)
    setSelectedId(null)
    queryClient.clear()
    queryClient.setQueryData(['me'], null)
  }, [queryClient])

  const refresh = useCallback(async () => {
    await queryClient.invalidateQueries({ queryKey: ['me'] })
  }, [queryClient])

  const value = useMemo<Session>(
    () => ({
      me,
      isLoading,
      organization,
      can: (permission) => organization?.permissions.includes(permission) ?? false,
      selectOrganization,
      signedIn,
      signOut,
      refresh,
    }),
    [me, isLoading, organization, selectOrganization, signedIn, signOut, refresh],
  )

  return <SessionContext.Provider value={value}>{children}</SessionContext.Provider>
}

export function useSession(): Session {
  const session = useContext(SessionContext)
  if (!session) throw new Error('useSession must be used inside SessionProvider')
  return session
}
