import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link, useNavigate, useSearchParams } from 'react-router-dom'
import { post } from '../../lib/api'
import type { Me } from '../../lib/types'
import { useSession } from '../../app/session'
import { Button, ErrorBox, SelectField, TextField } from '../../components/ui'
import { AuthLayout } from './AuthLayout'
import { passwordRule } from '../../lib/validation'

const schema = z
  .object({
    fullName: z.string().trim().min(2, 'Enter your full name.'),
    email: z.email('Enter a valid email address.'),
    password: passwordRule,
    organizationName: z.string().trim(),
    organizationType: z.enum(['ManagementCompany', 'IndividualLandlord']),
    invitationToken: z.string().trim(),
  })
  .refine((v) => v.invitationToken.length > 0 || v.organizationName.length >= 2, {
    path: ['organizationName'],
    message: 'Enter your company name, or your own name if you manage your own property.',
  })
type Values = z.infer<typeof schema>

export function RegisterPage() {
  const [params] = useSearchParams()
  const invitationToken = params.get('invite') ?? ''
  const joining = invitationToken.length > 0
  const { signedIn } = useSession()
  const navigate = useNavigate()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: {
      fullName: '',
      email: params.get('email') ?? '',
      password: '',
      organizationName: '',
      organizationType: 'ManagementCompany',
      invitationToken,
    },
  })
  const signUp = useMutation({
    mutationFn: (v: Values) =>
      post<Me>('/api/v1/auth/register', {
        ...v,
        organizationName: joining ? null : v.organizationName,
        invitationToken: joining ? v.invitationToken : null,
      }),
    onSuccess: (me) => {
      signedIn(me)
      navigate('/app', { replace: true })
    },
  })

  return (
    <AuthLayout
      title={joining ? 'Join your team' : 'Create your account'}
      subtitle={joining ? 'Create a login to accept your invitation.' : 'Start a free 30-day trial. No card needed.'}
      footer={
        <>
          Already have an account?{' '}
          <Link to="/login" className="font-medium text-brand-700 hover:underline">
            Sign in
          </Link>
        </>
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit((v) => signUp.mutate(v))} noValidate>
        <ErrorBox error={signUp.error} />
        <TextField label="Full name" autoComplete="name" {...register('fullName')} error={formState.errors.fullName?.message} />
        <TextField
          label="Email"
          type="email"
          autoComplete="email"
          {...register('email')}
          error={formState.errors.email?.message}
          hint={joining ? 'Use the email address the invitation was sent to.' : undefined}
        />
        <TextField
          label="Password"
          type="password"
          autoComplete="new-password"
          hint="At least 10 characters, with a lowercase letter and a number."
          {...register('password')}
          error={formState.errors.password?.message}
        />
        {!joining && (
          <>
            <SelectField label="I am" {...register('organizationType')}>
              <option value="ManagementCompany">A property management company</option>
              <option value="IndividualLandlord">A landlord managing my own property</option>
            </SelectField>
            <TextField
              label="Company or business name"
              autoComplete="organization"
              {...register('organizationName')}
              error={formState.errors.organizationName?.message}
            />
          </>
        )}
        <input type="hidden" {...register('invitationToken')} />
        <Button type="submit" className="w-full" disabled={signUp.isPending}>
          {signUp.isPending ? 'Creating account…' : joining ? 'Create login and join' : 'Create account'}
        </Button>
      </form>
    </AuthLayout>
  )
}
