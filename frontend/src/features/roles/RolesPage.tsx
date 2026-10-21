import { useState, type FormEvent, type ReactNode } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, post, put } from '../../lib/api'
import type { PermissionGroup, Role } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions, permissionGroupLabels, permissionLabels, twoFactorPermissions } from '../../lib/permissions'
import { Badge, Button, Card, ErrorBox, PageHeader, TextAreaField, TextField } from '../../components/ui'
import { rolesKey, useRoles } from './roles'

/** Built-in roles, and the organization's own roles made from the permission list (design 5). */
export function RolesPage() {
  const { can } = useSession()
  const canManage = can(Permissions.usersManage)
  const roles = useRoles()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<Role | 'new' | null>(null)
  const remove = useMutation({
    mutationFn: (role: Role) => del(`/api/v1/roles/${role.id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: rolesKey }),
  })

  const builtIn = roles.data?.filter((r) => !r.isCustom) ?? []
  const custom = roles.data?.filter((r) => r.isCustom) ?? []

  return (
    <>
      <PageHeader
        title="Roles"
        description="What each role lets people do. Make your own roles when the built-in ones don't fit how your team works."
        actions={canManage && editing === null && <Button onClick={() => setEditing('new')}>New role</Button>}
      />
      <div className="space-y-6">
        {editing !== null && <RoleForm role={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
        <ErrorBox error={roles.error ?? remove.error} />
        <Card title="Your roles">
          {custom.length === 0 ? (
            <p className="text-sm text-slate-600">No roles of your own yet.{canManage && ' Use "New role" to make one.'}</p>
          ) : (
            <ul className="divide-y divide-slate-100">
              {custom.map((r) => (
                <RoleRow
                  key={r.key}
                  role={r}
                  actions={
                    canManage && (
                      <>
                        <Button variant="secondary" onClick={() => setEditing(r)} aria-label={`Edit ${r.name}`}>
                          Edit
                        </Button>
                        <Button
                          variant="ghost"
                          aria-label={`Delete ${r.name}`}
                          onClick={() => {
                            if (confirm(`Delete the ${r.name} role?`)) remove.mutate(r)
                          }}
                        >
                          Delete
                        </Button>
                      </>
                    )
                  }
                />
              ))}
            </ul>
          )}
        </Card>
        <Card title="Built-in roles">
          <ul className="divide-y divide-slate-100">
            {builtIn.map((r) => (
              <RoleRow key={r.key} role={r} />
            ))}
          </ul>
        </Card>
      </div>
    </>
  )
}

function RoleRow({ role, actions }: { role: Role; actions?: ReactNode }) {
  return (
    <li className="py-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="flex flex-wrap items-center gap-2 text-sm font-medium">
            {role.name}
            {role.requiresTwoFactor && <Badge tone="amber">Needs two-step sign-in</Badge>}
          </p>
          {role.description && <p className="text-sm text-slate-600">{role.description}</p>}
          <p className="mt-1 text-xs text-slate-500">
            {role.memberCount} {role.memberCount === 1 ? 'member' : 'members'} · {role.permissions.length}{' '}
            {role.permissions.length === 1 ? 'permission' : 'permissions'}
          </p>
        </div>
        {actions && <div className="flex gap-2">{actions}</div>}
      </div>
      <details className="mt-2">
        <summary className="cursor-pointer text-xs font-medium text-brand-700">What this role can do</summary>
        <ul className="mt-2 list-disc space-y-0.5 pl-5 text-xs text-slate-600">
          {role.permissions.map((p) => (
            <li key={p}>{permissionLabels[p] ?? p}</li>
          ))}
        </ul>
      </details>
    </li>
  )
}

function RoleForm({ role, onDone }: { role: Role | null; onDone: () => void }) {
  const { can } = useSession()
  const queryClient = useQueryClient()
  const groups = useQuery({ queryKey: ['permission-groups'], queryFn: () => get<PermissionGroup[]>('/api/v1/roles/permissions') })
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
  const [chosen, setChosen] = useState<Set<string>>(new Set(role?.permissions ?? [Permissions.organizationView]))
  const save = useMutation({
    mutationFn: () => {
      const body = { name, description, permissions: [...chosen] }
      return role ? put<Role>(`/api/v1/roles/${role.id}`, body) : post<Role>('/api/v1/roles', body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: rolesKey })
      onDone()
    },
  })
  const toggle = (code: string) =>
    setChosen((current) => {
      const next = new Set(current)
      if (next.has(code)) next.delete(code)
      else next.add(code)
      return next
    })
  const needsTwoFactor = [...chosen].some((p) => twoFactorPermissions.includes(p))

  const submit = (e: FormEvent) => {
    e.preventDefault()
    save.mutate()
  }

  return (
    <Card title={role ? `Edit ${role.name}` : 'New role'}>
      <form className="space-y-5" onSubmit={submit} noValidate>
        <ErrorBox error={groups.error ?? save.error} />
        <div className="grid gap-4 sm:grid-cols-2">
          <TextField label="Role name" value={name} maxLength={100} onChange={(e) => setName(e.target.value)} required />
          <TextAreaField
            label="Description (optional)"
            rows={2}
            maxLength={500}
            value={description}
            onChange={(e) => setDescription(e.target.value)}
          />
        </div>
        <div className="space-y-4">
          <p className="text-sm text-slate-600">
            Choose what people with this role can do. You can only give permissions you have yourself. Branch limits on each member still
            apply.
          </p>
          <div className="grid gap-4 sm:grid-cols-2">
            {groups.data?.map((g) => (
              <fieldset key={g.group} className="rounded-md p-3 ring-1 ring-slate-200">
                <legend className="px-1 text-sm font-semibold text-slate-900">{permissionGroupLabels[g.group] ?? g.group}</legend>
                <div className="space-y-1.5">
                  {g.permissions.map((code) => {
                    const allowed = can(code)
                    return (
                      <label key={code} className={`flex items-start gap-2 text-sm ${allowed ? 'text-slate-700' : 'text-slate-400'}`}>
                        <input
                          type="checkbox"
                          className="mt-0.5"
                          checked={chosen.has(code)}
                          disabled={!allowed && !chosen.has(code)}
                          onChange={() => toggle(code)}
                        />
                        <span>{permissionLabels[code] ?? code}</span>
                      </label>
                    )
                  })}
                </div>
              </fieldset>
            ))}
          </div>
        </div>
        {needsTwoFactor && (
          <p className="rounded-md bg-amber-50 p-3 text-sm text-amber-900" role="status">
            Some of these permissions are admin or finance powers, so people with this role must turn on two-step sign-in.
          </p>
        )}
        <div className="flex gap-2">
          <Button type="submit" disabled={save.isPending || !name.trim() || chosen.size === 0}>
            {save.isPending ? 'Saving…' : role ? 'Save role' : 'Create role'}
          </Button>
          <Button type="button" variant="secondary" onClick={onDone}>
            Cancel
          </Button>
        </div>
      </form>
    </Card>
  )
}
