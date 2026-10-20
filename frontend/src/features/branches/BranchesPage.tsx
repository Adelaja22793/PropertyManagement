import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { get, post, put } from '../../lib/api'
import type { Branch } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { Badge, Button, Card, Empty, ErrorBox, PageHeader, TextField } from '../../components/ui'

const schema = z.object({
  name: z.string().trim().min(2, 'Enter a branch name.').max(200),
  address: z.string().trim().max(500),
  isActive: z.boolean(),
})
type Values = z.infer<typeof schema>

export function BranchesPage() {
  const { can } = useSession()
  const canManage = can(Permissions.branchesManage)
  const branches = useQuery({ queryKey: ['branches'], queryFn: () => get<Branch[]>('/api/v1/branches') })
  const [editing, setEditing] = useState<Branch | 'new' | null>(null)

  return (
    <>
      <PageHeader
        title="Branches"
        description="Offices within your company. Staff can be limited to the branches they work in."
        actions={canManage && editing === null && <Button onClick={() => setEditing('new')}>Add branch</Button>}
      />
      {editing !== null && <BranchForm branch={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      <Card>
        <ErrorBox error={branches.error} />
        {branches.data?.length === 0 && <Empty>No branches yet.</Empty>}
        {!!branches.data?.length && (
          <ul className="divide-y divide-slate-100">
            {branches.data.map((b) => (
              <li key={b.id} className="flex items-center justify-between gap-4 py-3">
                <div>
                  <p className="text-sm font-medium">
                    {b.name} {!b.isActive && <Badge tone="amber">Inactive</Badge>}
                  </p>
                  {b.address && <p className="text-sm text-slate-600">{b.address}</p>}
                </div>
                {canManage && (
                  <Button variant="ghost" onClick={() => setEditing(b)}>
                    Edit
                  </Button>
                )}
              </li>
            ))}
          </ul>
        )}
      </Card>
    </>
  )
}

function BranchForm({ branch, onDone }: { branch: Branch | null; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: branch?.name ?? '', address: branch?.address ?? '', isActive: branch?.isActive ?? true },
  })
  const save = useMutation({
    mutationFn: (v: Values) => (branch ? put<Branch>(`/api/v1/branches/${branch.id}`, v) : post<Branch>('/api/v1/branches', v)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['branches'] })
      onDone()
    },
  })

  return (
    <div className="mb-6">
      <Card title={branch ? `Edit ${branch.name}` : 'New branch'}>
        <form className="space-y-4" onSubmit={handleSubmit((v) => save.mutate(v))} noValidate>
          <ErrorBox error={save.error} />
          <div className="grid gap-4 sm:grid-cols-2">
            <TextField label="Name" {...register('name')} error={formState.errors.name?.message} />
            <TextField label="Address" {...register('address')} error={formState.errors.address?.message} />
          </div>
          {branch && (
            <label className="flex items-center gap-2 text-sm text-slate-700">
              <input type="checkbox" {...register('isActive')} /> Active
            </label>
          )}
          <div className="flex gap-2">
            <Button type="submit" disabled={save.isPending}>
              Save
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
