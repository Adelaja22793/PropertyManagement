import { useState } from 'react'
import { useForm } from 'react-hook-form'
import { zodResolver } from '@hookform/resolvers/zod'
import { z } from 'zod'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { ApiError, get, post } from '../../lib/api'
import type { BankAccount } from '../../lib/types'
import { useSession } from '../../app/session'
import { Permissions } from '../../lib/permissions'
import { enumLabel, formatDate } from '../../lib/format'
import { statusTone } from '../../lib/tones'
import { Badge, Button, Card, Empty, ErrorBox, TextField } from '../../components/ui'

/** Payout accounts. A new account needs a second person with approval rights before it is used (maker-checker). */
export function BankAccountsPanel({ ownerId }: { ownerId: string }) {
  const { can, me } = useSession()
  const canAdd = can(Permissions.ownersManage)
  const canDecide = can(Permissions.ownersApproveBankAccounts)
  const [adding, setAdding] = useState(false)
  const accounts = useQuery({
    queryKey: ['owners', ownerId, 'bank-accounts'],
    queryFn: () => get<BankAccount[]>(`/api/v1/owners/${ownerId}/bank-accounts`),
  })

  return (
    <Card
      title="Payout bank accounts"
      actions={
        canAdd &&
        !adding && (
          <Button variant="secondary" onClick={() => setAdding(true)}>
            Add account
          </Button>
        )
      }
    >
      {adding && <AddAccountForm ownerId={ownerId} onDone={() => setAdding(false)} />}
      <ErrorBox error={accounts.error} />
      {accounts.data?.length === 0 && <Empty>No bank accounts yet.</Empty>}
      <ul className="divide-y divide-slate-100">
        {accounts.data?.map((a) => (
          <AccountRow key={a.id} ownerId={ownerId} account={a} canDecide={canDecide} addedByMe={a.requestedByUserId === me?.userId} />
        ))}
      </ul>
    </Card>
  )
}

function AccountRow({ ownerId, account: a, canDecide, addedByMe }: { ownerId: string; account: BankAccount; canDecide: boolean; addedByMe: boolean }) {
  const queryClient = useQueryClient()
  const [rejecting, setRejecting] = useState(false)
  const [note, setNote] = useState('')
  const decide = useMutation({
    mutationFn: (approve: boolean) =>
      post<BankAccount>(`/api/v1/owners/${ownerId}/bank-accounts/${a.id}/decision`, { approve, note: note.trim() || null }),
    onSuccess: async () => {
      setRejecting(false)
      await queryClient.invalidateQueries({ queryKey: ['owners'] })
    },
  })
  const pending = a.status === 'PendingApproval'
  const makerChecker = decide.error instanceof ApiError && decide.error.code === 'maker_checker' ? decide.error : null

  return (
    <li className="space-y-2 py-3">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <p className="text-sm font-medium">
            {a.bankName} · <span className="font-mono">{a.accountNumber}</span>
          </p>
          <p className="text-sm text-slate-600">{a.accountName}</p>
          <p className="text-xs text-slate-500">
            Added by {addedByMe ? 'you' : (a.requestedByName ?? 'someone')} {formatDate(a.requestedAtUtc)}
            {a.decidedAtUtc && ` · ${a.status === 'Approved' ? 'approved' : 'decided'} by ${a.decidedByName ?? 'someone'} ${formatDate(a.decidedAtUtc)}`}
          </p>
          {a.decisionNote && <p className="text-xs text-slate-500">Note: {a.decisionNote}</p>}
        </div>
        <Badge tone={statusTone(a.status)}>{enumLabel(a.status)}</Badge>
      </div>
      {pending && canDecide && !rejecting && (
        <div className="flex flex-wrap items-center gap-2">
          <Button disabled={decide.isPending} onClick={() => decide.mutate(true)}>
            Approve
          </Button>
          <Button variant="danger" disabled={decide.isPending} onClick={() => setRejecting(true)}>
            Reject
          </Button>
          {addedByMe && <span className="text-xs text-slate-500">You added this account, so another approver must decide it.</span>}
        </div>
      )}
      {pending && canDecide && rejecting && (
        <form
          className="flex flex-wrap items-end gap-2"
          onSubmit={(e) => {
            e.preventDefault()
            decide.mutate(false)
          }}
        >
          <div className="min-w-0 flex-1 basis-48">
            <TextField label="Reason (optional)" id={`reject-note-${a.id}`} value={note} onChange={(e) => setNote(e.target.value)} maxLength={500} />
          </div>
          <Button type="submit" variant="danger" disabled={decide.isPending}>
            Reject account
          </Button>
          <Button type="button" variant="secondary" onClick={() => setRejecting(false)}>
            Cancel
          </Button>
        </form>
      )}
      {makerChecker ? (
        <div role="alert" className="rounded-md bg-amber-50 p-3 text-sm text-amber-900 ring-1 ring-amber-200">
          <p className="font-medium">{makerChecker.message}</p>
          <p className="mt-1">Ask another finance approver to review this account.</p>
        </div>
      ) : (
        <ErrorBox error={decide.error} />
      )}
    </li>
  )
}

const schema = z.object({
  bankName: z.string().trim().min(2, 'Enter the bank name.').max(100),
  accountNumber: z
    .string()
    .trim()
    .regex(/^\d{10}$/, 'Account number must be 10 digits (NUBAN).'),
  accountName: z.string().trim().min(2, 'Enter the account name.').max(200),
})
type Values = z.infer<typeof schema>

function AddAccountForm({ ownerId, onDone }: { ownerId: string; onDone: () => void }) {
  const queryClient = useQueryClient()
  const { register, handleSubmit, formState } = useForm<Values>({
    resolver: zodResolver(schema),
    defaultValues: { bankName: '', accountNumber: '', accountName: '' },
  })
  const add = useMutation({
    mutationFn: (v: Values) => post<BankAccount>(`/api/v1/owners/${ownerId}/bank-accounts`, v),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: ['owners'] })
      onDone()
    },
  })
  const errors = formState.errors
  return (
    <form className="mb-4 space-y-4 rounded-md bg-slate-50 p-4 ring-1 ring-slate-200" onSubmit={handleSubmit((v) => add.mutate(v))} noValidate>
      <p className="text-sm text-slate-600">The account is used for payouts only after another person approves it.</p>
      <ErrorBox error={add.error} />
      <div className="grid gap-4 sm:grid-cols-3">
        <TextField label="Bank name" {...register('bankName')} error={errors.bankName?.message} />
        <TextField label="Account number" inputMode="numeric" maxLength={10} {...register('accountNumber')} error={errors.accountNumber?.message} />
        <TextField label="Account name" {...register('accountName')} error={errors.accountName?.message} />
      </div>
      <div className="flex gap-2">
        <Button type="submit" disabled={add.isPending}>
          Add account
        </Button>
        <Button type="button" variant="secondary" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
