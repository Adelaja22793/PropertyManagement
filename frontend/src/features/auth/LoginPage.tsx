import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link, useLocation, useNavigate } from 'react-router-dom'
import { ApiError, post } from '../../lib/api'
import type { Me, TwoFactorChallenge } from '../../lib/types'
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
  // Set once the password is accepted and the account needs a code from the authenticator app.
  const [secondStep, setSecondStep] = useState<{ rememberMe: boolean } | null>(null)
  const finish = (me: Me) => {
    signedIn(me)
    const back = (location.state as { from?: string } | null)?.from
    navigate(back ?? '/app', { replace: true })
  }
  const login = useMutation({
    mutationFn: (values: Values) => post<Me | TwoFactorChallenge>('/api/v1/auth/login', values),
    onSuccess: (result, values) => {
      if ('requiresTwoFactor' in result) setSecondStep({ rememberMe: values.rememberMe })
      else finish(result)
    },
  })

  if (secondStep)
    return <CodeStep rememberMe={secondStep.rememberMe} onDone={finish} onRestart={() => setSecondStep(null)} />

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

/** The second step of sign-in: a code from the authenticator app, or a recovery code. */
function CodeStep({ rememberMe, onDone, onRestart }: { rememberMe: boolean; onDone: (me: Me) => void; onRestart: () => void }) {
  const [useRecovery, setUseRecovery] = useState(false)
  const [code, setCode] = useState('')
  const verify = useMutation({
    mutationFn: () => post<Me>(useRecovery ? '/api/v1/auth/login/recovery-code' : '/api/v1/auth/login/two-factor', { code: code.trim(), rememberMe }),
    onSuccess: onDone,
    onError: (error) => {
      if (error instanceof ApiError && error.code === 'two_factor_expired') onRestart()
    },
  })
  return (
    <AuthLayout
      title="Two-step sign-in"
      subtitle={
        useRecovery
          ? 'Enter one of the recovery codes you saved when you set up two-step sign-in. Each code works once.'
          : 'Open your authenticator app and enter the 6-digit code for Property Management.'
      }
    >
      <form
        className="space-y-4"
        onSubmit={(e) => {
          e.preventDefault()
          if (code.trim()) verify.mutate()
        }}
        noValidate
      >
        <ErrorBox error={verify.error} />
        <TextField
          key={useRecovery ? 'recovery' : 'app'}
          label={useRecovery ? 'Recovery code' : 'Code'}
          autoComplete="one-time-code"
          inputMode={useRecovery ? 'text' : 'numeric'}
          autoFocus
          value={code}
          onChange={(e) => setCode(e.target.value)}
          maxLength={20}
        />
        <Button type="submit" className="w-full" disabled={verify.isPending || !code.trim()}>
          {verify.isPending ? 'Checking…' : 'Sign in'}
        </Button>
        <div className="flex flex-wrap justify-between gap-2 text-sm">
          <button
            type="button"
            className="font-medium text-brand-700 hover:underline"
            onClick={() => {
              setUseRecovery((r) => !r)
              setCode('')
              verify.reset()
            }}
          >
            {useRecovery ? 'Use a code from the app' : 'Lost your phone? Use a recovery code'}
          </button>
          <button type="button" className="text-slate-600 hover:underline" onClick={onRestart}>
            Start again
          </button>
        </div>
      </form>
    </AuthLayout>
  )
}
