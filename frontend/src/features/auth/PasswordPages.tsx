import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation } from '@tanstack/react-query'
import { Link, useSearchParams } from 'react-router-dom'
import { post } from '../../lib/api'
import { Button, ErrorBox, TextField } from '../../components/ui'
import { AuthLayout } from './AuthLayout'
import { passwordRule } from '../../lib/validation'

const forgotSchema = z.object({ email: z.email('Enter a valid email address.') })

export function ForgotPasswordPage() {
  const { register, handleSubmit, formState } = useForm<z.infer<typeof forgotSchema>>({
    resolver: zodResolver(forgotSchema),
    defaultValues: { email: '' },
  })
  const request = useMutation({ mutationFn: (v: z.infer<typeof forgotSchema>) => post('/api/v1/auth/forgot-password', v) })

  return (
    <AuthLayout title="Reset your password" footer={<Link to="/login" className="font-medium text-brand-700 hover:underline">Back to sign in</Link>}>
      {request.isSuccess ? (
        <div className="space-y-4 text-sm text-slate-700">
          <p>If an account exists for that email, we've sent a reset code to it.</p>
          <Link to="/reset-password" className="font-medium text-brand-700 hover:underline">
            I have a reset code
          </Link>
        </div>
      ) : (
        <form className="space-y-4" onSubmit={handleSubmit((v) => request.mutate(v))} noValidate>
          <ErrorBox error={request.error} />
          <TextField label="Email" type="email" autoComplete="email" {...register('email')} error={formState.errors.email?.message} />
          <Button type="submit" className="w-full" disabled={request.isPending}>
            Send reset code
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}

const resetSchema = z.object({
  email: z.email('Enter a valid email address.'),
  token: z.string().trim().min(1, 'Paste the code from the email.'),
  newPassword: passwordRule,
})

export function ResetPasswordPage() {
  const [params] = useSearchParams()
  const { register, handleSubmit, formState } = useForm<z.infer<typeof resetSchema>>({
    resolver: zodResolver(resetSchema),
    defaultValues: { email: params.get('email') ?? '', token: params.get('token') ?? '', newPassword: '' },
  })
  const reset = useMutation({ mutationFn: (v: z.infer<typeof resetSchema>) => post('/api/v1/auth/reset-password', v) })

  return (
    <AuthLayout title="Choose a new password" footer={<Link to="/login" className="font-medium text-brand-700 hover:underline">Back to sign in</Link>}>
      {reset.isSuccess ? (
        <p className="text-sm text-slate-700">
          Your password has been changed.{' '}
          <Link to="/login" className="font-medium text-brand-700 hover:underline">
            Sign in
          </Link>
        </p>
      ) : (
        <form className="space-y-4" onSubmit={handleSubmit((v) => reset.mutate(v))} noValidate>
          <ErrorBox error={reset.error} />
          <TextField label="Email" type="email" {...register('email')} error={formState.errors.email?.message} />
          <TextField label="Reset code" {...register('token')} error={formState.errors.token?.message} />
          <TextField label="New password" type="password" autoComplete="new-password" {...register('newPassword')} error={formState.errors.newPassword?.message} />
          <Button type="submit" className="w-full" disabled={reset.isPending}>
            Change password
          </Button>
        </form>
      )}
    </AuthLayout>
  )
}
