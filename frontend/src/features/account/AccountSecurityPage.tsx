import { useEffect, useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import QRCode from 'qrcode'
import { get, post } from '../../lib/api'
import type { TwoFactorStatus } from '../../lib/types'
import { useSession } from '../../app/session'
import { formatDate } from '../../lib/format'
import { Badge, Button, Card, ErrorBox, PageHeader, TextField } from '../../components/ui'
import { isOverdue, recoveryCodesFile, twoFactorDeadline } from './twoFactor'

interface Setup {
  sharedKey: string
  authenticatorUri: string
}

const statusKey = ['two-factor']

/** Two-step sign-in for the signed-in person (design 11.1). */
export function AccountSecurityPage() {
  const { me, organization } = useSession()
  const status = useQuery({ queryKey: statusKey, queryFn: () => get<TwoFactorStatus>('/api/v1/auth/two-factor') })
  const [codes, setCodes] = useState<string[] | null>(null)
  const s = status.data
  const deadline = me && organization ? twoFactorDeadline(me, organization) : null

  return (
    <>
      <PageHeader
        title="Security"
        description="Protect your account with a code from an authenticator app on your phone when you sign in."
      />
      <div className="space-y-6">
        <ErrorBox error={status.error} />
        {codes && <RecoveryCodes codes={codes} email={me?.email ?? ''} onDone={() => setCodes(null)} />}
        {s && !codes && (
          <Card
            title="Two-step sign-in"
            actions={s.enabled ? <Badge tone="green">On</Badge> : <Badge tone={s.required ? 'amber' : 'slate'}>Off</Badge>}
          >
            {s.enabled ? (
              <Enabled status={s} onCodes={setCodes} />
            ) : (
              <>
                {s.required && (
                  <p className="mb-4 rounded-md bg-amber-50 p-3 text-sm text-amber-900">
                    Your role needs two-step sign-in
                    {deadline
                      ? !isOverdue(deadline)
                        ? `. Turn it on by ${formatDate(deadline.toISOString())}.`
                        : ', and it is now overdue.'
                      : '.'}
                  </p>
                )}
                <TurnOn onCodes={setCodes} />
              </>
            )}
          </Card>
        )}
      </div>
    </>
  )
}

function TurnOn({ onCodes }: { onCodes: (codes: string[]) => void }) {
  const { refresh } = useSession()
  const queryClient = useQueryClient()
  const [code, setCode] = useState('')
  const setup = useMutation({ mutationFn: () => post<Setup>('/api/v1/auth/two-factor/setup') })
  const enable = useMutation({
    mutationFn: () => post<{ recoveryCodes: string[] }>('/api/v1/auth/two-factor/enable', { code }),
    onSuccess: async (result) => {
      onCodes(result.recoveryCodes)
      await Promise.all([queryClient.invalidateQueries({ queryKey: statusKey }), refresh()])
    },
  })

  if (!setup.data) {
    return (
      <div className="space-y-3">
        <p className="text-sm text-slate-600">
          After your password, you'll type a six-digit code from an app such as Google Authenticator or Microsoft Authenticator. Someone who
          learns your password still can't get in without your phone.
        </p>
        <ErrorBox error={setup.error} />
        <Button onClick={() => setup.mutate()} disabled={setup.isPending}>
          {setup.isPending ? 'Starting…' : 'Set up two-step sign-in'}
        </Button>
      </div>
    )
  }

  const submit = (e: FormEvent) => {
    e.preventDefault()
    enable.mutate()
  }

  return (
    <form onSubmit={submit} className="space-y-4">
      <ol className="list-decimal space-y-4 pl-5 text-sm text-slate-700">
        <li>
          <p>Open your authenticator app and scan this code.</p>
          <QrImage uri={setup.data.authenticatorUri} />
          <p className="mt-2 text-slate-600">Can't scan it? Enter this key in the app instead:</p>
          <p className="mt-1 font-mono text-base tracking-wider text-slate-900" data-testid="shared-key">
            {setup.data.sharedKey}
          </p>
        </li>
        <li>
          <p className="mb-2">Type the six-digit code the app shows.</p>
          <div className="max-w-xs">
            <TextField
              label="Code from the app"
              inputMode="numeric"
              autoComplete="one-time-code"
              value={code}
              onChange={(e) => setCode(e.target.value)}
              required
            />
          </div>
        </li>
      </ol>
      <ErrorBox error={enable.error} />
      <Button type="submit" disabled={enable.isPending || code.trim().length < 6}>
        {enable.isPending ? 'Checking…' : 'Turn on'}
      </Button>
    </form>
  )
}

function QrImage({ uri }: { uri: string }) {
  const [src, setSrc] = useState<string | null>(null)
  useEffect(() => {
    let live = true
    QRCode.toDataURL(uri, { margin: 1, width: 192 })
      .then((url) => live && setSrc(url))
      .catch(() => live && setSrc(null))
    return () => {
      live = false
    }
  }, [uri])
  return src ? (
    <img src={src} alt="QR code for your authenticator app" width={192} height={192} className="mt-2 rounded-md ring-1 ring-slate-200" />
  ) : (
    <div className="mt-2 h-48 w-48 rounded-md bg-slate-100" />
  )
}

function Enabled({ status, onCodes }: { status: TwoFactorStatus; onCodes: (codes: string[]) => void }) {
  const { refresh } = useSession()
  const queryClient = useQueryClient()
  const [code, setCode] = useState('')
  const [password, setPassword] = useState('')
  const regenerate = useMutation({
    mutationFn: () => post<{ recoveryCodes: string[] }>('/api/v1/auth/two-factor/recovery-codes', { code }),
    onSuccess: async (result) => {
      setCode('')
      onCodes(result.recoveryCodes)
      await queryClient.invalidateQueries({ queryKey: statusKey })
    },
  })
  const disable = useMutation({
    mutationFn: () => post('/api/v1/auth/two-factor/disable', { password }),
    onSuccess: async () => {
      setPassword('')
      await Promise.all([queryClient.invalidateQueries({ queryKey: statusKey }), refresh()])
    },
  })

  return (
    <div className="space-y-6">
      <p className="text-sm text-slate-600">
        Each time you sign in, you'll be asked for a code from your authenticator app. You have{' '}
        <strong>
          {status.recoveryCodesLeft} recovery {status.recoveryCodesLeft === 1 ? 'code' : 'codes'}
        </strong>{' '}
        left for when your phone isn't with you.
      </p>
      <form
        className="space-y-3 border-t border-slate-100 pt-4"
        onSubmit={(e) => {
          e.preventDefault()
          regenerate.mutate()
        }}
      >
        <h3 className="text-sm font-semibold text-slate-900">New recovery codes</h3>
        <p className="text-sm text-slate-600">Your old codes stop working. Type a code from the app to confirm.</p>
        <div className="max-w-xs">
          <TextField
            label="Code from the app"
            id="regenerate-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
          />
        </div>
        <ErrorBox error={regenerate.error} />
        <Button type="submit" variant="secondary" disabled={regenerate.isPending || code.trim().length < 6}>
          {regenerate.isPending ? 'Making codes…' : 'Make new recovery codes'}
        </Button>
      </form>
      <div className="space-y-3 border-t border-slate-100 pt-4">
        <h3 className="text-sm font-semibold text-slate-900">Turn off</h3>
        {status.required ? (
          <p className="text-sm text-slate-600">
            Your role in one of your organizations needs two-step sign-in, so it can't be turned off.
          </p>
        ) : (
          <form
            className="space-y-3"
            onSubmit={(e) => {
              e.preventDefault()
              disable.mutate()
            }}
          >
            <div className="max-w-xs">
              <TextField
                label="Your password"
                id="disable-password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
              />
            </div>
            <ErrorBox error={disable.error} />
            <Button type="submit" variant="danger" disabled={disable.isPending || !password}>
              {disable.isPending ? 'Turning off…' : 'Turn off two-step sign-in'}
            </Button>
          </form>
        )}
      </div>
    </div>
  )
}

function RecoveryCodes({ codes, email, onDone }: { codes: string[]; email: string; onDone: () => void }) {
  const [copied, setCopied] = useState(false)
  const text = recoveryCodesFile(email, codes)
  const save = () => {
    const url = URL.createObjectURL(new Blob([text], { type: 'text/plain' }))
    const link = document.createElement('a')
    link.href = url
    link.download = 'recovery-codes.txt'
    document.body.appendChild(link)
    link.click()
    link.remove()
    setTimeout(() => URL.revokeObjectURL(url), 1000)
  }
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(text)
      setCopied(true)
    } catch {
      setCopied(false)
    }
  }

  return (
    <Card title="Save your recovery codes">
      <div className="space-y-4">
        <p className="text-sm text-slate-600">
          If you lose your phone, each code signs you in once. This is the only time they're shown, so save or print them now and keep them
          somewhere safe.
        </p>
        <ul aria-label="Recovery codes" className="grid max-w-md grid-cols-2 gap-2 font-mono text-sm text-slate-900">
          {codes.map((c) => (
            <li key={c} className="rounded-md bg-slate-50 px-3 py-1.5 ring-1 ring-slate-200">
              {c}
            </li>
          ))}
        </ul>
        <div className="flex flex-wrap gap-2">
          <Button variant="secondary" onClick={save}>
            Download
          </Button>
          <Button variant="secondary" onClick={copy}>
            {copied ? 'Copied' : 'Copy'}
          </Button>
          <Button onClick={onDone}>I've saved them</Button>
        </div>
      </div>
    </Card>
  )
}
