import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { post } from '../../lib/api'
import type { Me } from '../../lib/types'
import { useSession } from '../../app/session'
import { Button, ErrorBox, TextField } from '../../components/ui'
import { AuthLayout } from './AuthLayout'

const schema = z.object({
  email: z.email('Enter a valid email address.'),
  password: z.string().min(1, 'Enter your password.'),
  rememberMe: z.boolean(),
})
type Values = z.infer<typeof schema>

export function LoginPage() {
  const { signedIn } = useSession()
  const navigate = useNavigate()
  const location = useLocation()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { email: '', password: '', rememberMe: false },
  })
  const login = useMutation({
    mutationFn: (values: Values) => post<Me>('/api/v1/auth/login', values),
    onSuccess: (me) => {
      signedIn(me)
      const back = (location.state as { from?: string } | null)?.from
      navigate(back ?? '/app', { replace: true })
    },
  })

  return (
    <AuthLayout
      title="Sign in"
      subtitle="Manage your properties, tenants and rent in one place."
      footer={
        <>
          New here?{' '}
          <Link to="/register" className="font-medium text-brand-700 hover:underline">
            Create an account
          </Link>
        </>
      }
    >
      <form className="space-y-4" onSubmit={handleSubmit((v) => login.mutate(v))} noValidate>
        <ErrorBox error={login.error} />
        <TextField label="Email" type="email" autoComplete="email" {...register('email')} error={formState.errors.email?.message} />
        <TextField
          label="Password"
          type="password"
          autoComplete="current-password"
          {...register('password')}
          error={formState.errors.password?.message}
        />
        <div className="flex items-center justify-between">
          <label className="flex items-center gap-2 text-sm text-slate-700">
            <input type="checkbox" className="rounded border-slate-300 text-brand-600" {...register('rememberMe')} />
            Keep me signed in
          </label>
          <Link to="/forgot-password" className="text-sm font-medium text-brand-700 hover:underline">
            Forgot password?
          </Link>
        </div>
        <Button type="submit" className="w-full" disabled={login.isPending}>
          {login.isPending ? 'Signing in…' : 'Sign in'}
        </Button>
      </form>
    </AuthLayout>
  )
}
