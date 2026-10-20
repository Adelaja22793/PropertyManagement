import { useQuery } from '@tanstack/react-query'
import { get } from '../../lib/api'
import type { Role } from '../../lib/types'

export const rolesKey = ['roles']

/** Built-in and custom roles of the selected organization. */
export function useRoles(enabled = true) {
  return useQuery({ queryKey: rolesKey, queryFn: () => get<Role[]>('/api/v1/roles'), enabled })
}

/** Custom role names by key, for labelling members and invitations. */
export function customRoleNames(roles: Role[] | undefined): Record<string, string> {
  return Object.fromEntries((roles ?? []).filter((r) => r.isCustom).map((r) => [r.key, r.name]))
}
