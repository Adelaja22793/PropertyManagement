import { useState, type FormEvent } from 'react'
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'
import { del, get, post, put } from '../../lib/api'
import {
  BillingFrequencies,
  NoticeTypes,
  type BillingFrequency,
  type LeaseSettings,
  type NoticePeriodRule,
  type NoticeType,
} from '../../lib/types'
import { NigerianStates } from '../../lib/nigeria'
import { Button, Card, ErrorBox, SelectField, TextField } from '../../components/ui'
import { formatDays, frequencyLabels, leaseSettingsKey, noticeTypeLabels, parseDays, ruleScope } from './leaseSettings'

/** Lease reminder days and the company's own notice periods (design 8.9). Editable with the settings permission. */
export function LeaseSettingsCard({ canEdit }: { canEdit: boolean }) {
  const settings = useQuery({ queryKey: leaseSettingsKey, queryFn: () => get<LeaseSettings>('/api/v1/lease-settings') })
  return (
    <div className="space-y-6">
      <ErrorBox error={settings.error} />
      {settings.data && (
        <>
          <RemindersCard settings={settings.data} canEdit={canEdit} />
          <NoticePeriodsCard settings={settings.data} canEdit={canEdit} />
        </>
      )}
    </div>
  )
}

function RemindersCard({ settings, canEdit }: { settings: LeaseSettings; canEdit: boolean }) {
  const queryClient = useQueryClient()
  const [staff, setStaff] = useState(formatDays(settings.staffReminderDays))
  const [tenantsOn, setTenantsOn] = useState(settings.tenantRemindersEnabled)
  const [tenant, setTenant] = useState(formatDays(settings.tenantReminderDays))
  const staffDays = parseDays(staff)
  const tenantDays = parseDays(tenant)
  const valid = typeof staffDays !== 'string' && (!tenantsOn || typeof tenantDays !== 'string')
  const save = useMutation({
    mutationFn: () =>
      put<LeaseSettings>('/api/v1/lease-settings', {
        staffReminderDays: staffDays,
        tenantRemindersEnabled: tenantsOn,
        tenantReminderDays: typeof tenantDays === 'string' ? [] : tenantDays,
      }),
    onSuccess: (saved) => {
      queryClient.setQueryData(leaseSettingsKey, saved)
      setStaff(formatDays(saved.staffReminderDays))
      setTenant(formatDays(saved.tenantReminderDays))
    },
  })
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (valid) save.mutate()
  }

  return (
    <Card title="Lease reminders">
      <form onSubmit={submit} className="space-y-4" noValidate>
        <fieldset disabled={!canEdit} className="space-y-4">
          <TextField
            label="Remind staff this many days before a lease ends"
            value={staff}
            onChange={(e) => setStaff(e.target.value)}
            error={typeof staffDays === 'string' ? staffDays : undefined}
            hint="Separate days with commas. The property's manager is emailed, or your admins if it has none."
          />
          <label className="flex items-start gap-2 text-sm">
            <input type="checkbox" className="mt-0.5" checked={tenantsOn} onChange={(e) => setTenantsOn(e.target.checked)} />
            <span>
              Also email tenants before their lease ends
              <span className="block text-xs text-slate-500">
                Sent to the email address on each tenant's record, asking them to tell you whether they will renew or move out.
              </span>
            </span>
          </label>
          {tenantsOn && (
            <TextField
              label="Remind tenants this many days before their lease ends"
              value={tenant}
              onChange={(e) => setTenant(e.target.value)}
              error={typeof tenantDays === 'string' ? tenantDays : undefined}
            />
          )}
        </fieldset>
        <ErrorBox error={save.error} />
        {canEdit && (
          <div className="flex items-center gap-3">
            <Button type="submit" disabled={save.isPending || !valid}>
              Save reminders
            </Button>
            {save.isSuccess && <span className="text-sm text-brand-700">Saved.</span>}
          </div>
        )}
      </form>
    </Card>
  )
}

function NoticePeriodsCard({ settings, canEdit }: { settings: LeaseSettings; canEdit: boolean }) {
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState<NoticePeriodRule | 'new' | null>(null)
  const remove = useMutation({
    mutationFn: (id: string) => del(`/api/v1/lease-settings/notice-periods/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: leaseSettingsKey }),
  })

  return (
    <Card
      title="Notice periods"
      actions={canEdit && editing === null && <Button onClick={() => setEditing('new')}>Add notice period</Button>}
    >
      <p className="mb-4 text-sm text-slate-600">
        The number of days suggested when staff record a notice. Set your own for each state where you manage property, as your lawyer
        advises. A lease's own agreed notice period still comes first.
      </p>
      {editing !== null && <RuleForm rule={editing === 'new' ? null : editing} onDone={() => setEditing(null)} />}
      <ErrorBox error={remove.error} />
      {settings.noticePeriods.length === 0 ? (
        <p className="text-sm text-slate-600">You haven't set any of your own yet, so the defaults below apply everywhere.</p>
      ) : (
        <ul className="divide-y divide-slate-100" aria-label="Your notice periods">
          {settings.noticePeriods.map((r) => (
            <li key={r.id} className="flex flex-wrap items-start justify-between gap-3 py-3">
              <div className="min-w-0">
                <p className="text-sm font-medium">
                  {noticeTypeLabels[r.type]}: {r.days} days
                </p>
                <p className="text-sm text-slate-600">{ruleScope(r.state, r.frequency)}</p>
                {r.basis && <p className="text-xs text-slate-500">{r.basis}</p>}
              </div>
              {canEdit && (
                <div className="flex gap-2">
                  <Button
                    variant="secondary"
                    onClick={() => setEditing(r)}
                    aria-label={`Edit ${noticeTypeLabels[r.type]}, ${ruleScope(r.state, r.frequency)}`}
                  >
                    Edit
                  </Button>
                  <Button
                    variant="ghost"
                    aria-label={`Remove ${noticeTypeLabels[r.type]}, ${ruleScope(r.state, r.frequency)}`}
                    onClick={() => {
                      if (confirm('Remove this notice period?')) remove.mutate(r.id)
                    }}
                  >
                    Remove
                  </Button>
                </div>
              )}
            </li>
          ))}
        </ul>
      )}
      <h3 className="mt-6 text-xs font-medium text-slate-500">Defaults when none of yours match (Lagos Tenancy Law 2011)</h3>
      <ul className="mt-1 space-y-1 text-sm text-slate-600">
        {settings.builtInNoticePeriods.map((d, i) => (
          <li key={i}>
            {noticeTypeLabels[d.type]}
            {d.frequency && `, ${frequencyLabels[d.frequency]}`}: {d.days} days
          </li>
        ))}
      </ul>
    </Card>
  )
}

function RuleForm({ rule, onDone }: { rule: NoticePeriodRule | null; onDone: () => void }) {
  const queryClient = useQueryClient()
  const [type, setType] = useState<NoticeType>(rule?.type ?? 'NoticeToQuit')
  const [state, setState] = useState(rule?.state ?? '')
  const [frequency, setFrequency] = useState<BillingFrequency | ''>(rule?.frequency ?? '')
  const [days, setDays] = useState(rule ? String(rule.days) : '')
  const [basis, setBasis] = useState(rule?.basis ?? '')
  const save = useMutation({
    mutationFn: () => {
      const body = { type, state: state || null, frequency: frequency || null, days: Number(days), basis: basis.trim() || null }
      return rule ? put(`/api/v1/lease-settings/notice-periods/${rule.id}`, body) : post('/api/v1/lease-settings/notice-periods', body)
    },
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: leaseSettingsKey })
      onDone()
    },
  })
  const daysValid = /^\d+$/.test(days) && Number(days) >= 1 && Number(days) <= 730
  const submit = (e: FormEvent) => {
    e.preventDefault()
    if (daysValid) save.mutate()
  }

  return (
    <form
      onSubmit={submit}
      className="mb-6 space-y-4 rounded-md bg-slate-50 p-4"
      aria-label={rule ? 'Edit notice period' : 'New notice period'}
    >
      <div className="grid gap-4 sm:grid-cols-2">
        <SelectField label="Notice" value={type} onChange={(e) => setType(e.target.value as NoticeType)}>
          {NoticeTypes.map((t) => (
            <option key={t} value={t}>
              {noticeTypeLabels[t]}
            </option>
          ))}
        </SelectField>
        <TextField
          label="Days"
          inputMode="numeric"
          value={days}
          onChange={(e) => setDays(e.target.value)}
          error={days && !daysValid ? 'Enter a number of days from 1 to 730.' : undefined}
        />
        <SelectField label="State" value={state} onChange={(e) => setState(e.target.value)}>
          <option value="">All states</option>
          {NigerianStates.map((s) => (
            <option key={s} value={s}>
              {s}
            </option>
          ))}
        </SelectField>
        <SelectField label="Rent frequency" value={frequency} onChange={(e) => setFrequency(e.target.value as BillingFrequency | '')}>
          <option value="">Any</option>
          {BillingFrequencies.map((f) => (
            <option key={f} value={f}>
              {frequencyLabels[f].replace(' rent', '').replace(/^./, (c) => c.toUpperCase())}
            </option>
          ))}
        </SelectField>
      </div>
      <TextField
        label="Legal basis (optional)"
        value={basis}
        onChange={(e) => setBasis(e.target.value)}
        maxLength={300}
        hint="Where the number comes from, such as the section of the state's tenancy law or your lawyer's advice. Staff see it."
      />
      <ErrorBox error={save.error} />
      <div className="flex gap-2">
        <Button type="submit" disabled={save.isPending || !daysValid}>
          {rule ? 'Save notice period' : 'Add notice period'}
        </Button>
        <Button type="button" variant="ghost" onClick={onDone}>
          Cancel
        </Button>
      </div>
    </form>
  )
}
