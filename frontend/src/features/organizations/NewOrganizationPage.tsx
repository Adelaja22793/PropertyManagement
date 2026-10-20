import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link, useNavigate } from 'react-router-dom'
import { post } from '../../lib/api'
import type { OrganizationSummary } from '../../lib/types'
import { useSession } from '../../app/session'
import { Button, ErrorBox, SelectField, TextField } from '../../components/ui'
import { AuthLayout } from '../auth/AuthLayout'

const schema = z.object({
  name: z.string().trim().min(2, 'Enter a name.').max(200),
  type: z.enum(['ManagementCompany', 'IndividualLandlord']),
})
type Values = z.infer<typeof schema>

export function NewOrganizationPage() {
  const { refresh, selectOrganization, me } = useSession()
  const navigate = useNavigate()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { name: '', type: 'ManagementCompany' },
  })
  const create = useMutation({
    mutationFn: (v: Values) => post<OrganizationSummary>('/api/v1/organizations', v),
    onSuccess: async (org) => {
      await refresh()
      selectOrganization(org.id)
      navigate('/app', { replace: true })
    },
  })

  return (
    <AuthLayout
      title="New organization"
      subtitle="You'll be its admin. It has its own properties, team and books."
      footer={me?.organizations.length ? <Link to="/app" className="font-medium text-brand-700 hover:underline">Back</Link> : undefined}
    >
      <form className="space-y-4" onSubmit={handleSubmit((v) => create.mutate(v))} noValidate>
        <ErrorBox error={create.error} />
        <SelectField label="Type" {...register('type')}>
          <option value="ManagementCompany">Property management company</option>
          <option value="IndividualLandlord">Landlord managing my own property</option>
        </SelectField>
        <TextField label="Name" {...register('name')} error={formState.errors.name?.message} />
        <Button type="submit" className="w-full" disabled={create.isPending}>
          Create organization
        </Button>
      </form>
    </AuthLayout>
  )
}
