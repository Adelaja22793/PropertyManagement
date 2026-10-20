import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { me, mockApi, renderApp } from '../../test/render'

const owner = {
  id: 'o1',
  type: 'Individual',
  name: 'Chief Okafor',
  email: 'okafor@example.com',
  phone: null,
  address: null,
  taxIdentificationNumber: null,
  preferredChannel: 'Email',
  notes: null,
  createdAtUtc: '2026-10-01T09:00:00',
  properties: [],
  hasPortalAccess: false,
}

const account = (id: string, status: string, requestedByUserId = 'user-2') => ({
  id,
  bankName: 'GTBank',
  accountNumber: '0123456789',
  accountName: 'Okafor Chukwuma',
  status,
  requestedByUserId,
  requestedByName: 'Ada Accountant',
  requestedAtUtc: '2026-10-01T09:00:00',
  decidedByUserId: null,
  decidedByName: null,
  decidedAtUtc: null,
  decisionNote: null,
})

function serve(permissions: string[], accounts: unknown[], decision?: { status: number; body?: unknown }) {
  return mockApi((url, init) => {
    if (url.endsWith('/auth/me')) {
      return {
        status: 200,
        body: me([{ id: 'org-1', name: 'Lekki Homes', slug: 'l', type: 'ManagementCompany', status: 'Active', roles: ['FinanceApprover'], permissions: ['Organization.View', ...permissions] }]),
      }
    }
    if (url.endsWith('/owners/o1')) return { status: 200, body: owner }
    if (url.endsWith('/bank-accounts')) return { status: 200, body: accounts }
    if (url.endsWith('/decision') && init.method === 'POST') return decision ?? { status: 200, body: {} }
    return { status: 200, body: [] }
  })
}

describe('owner bank accounts', () => {
  it('shows approve and reject only to approvers, and only for pending accounts', async () => {
    serve(['Owners.View', 'Owners.ApproveBankAccounts'], [account('a1', 'PendingApproval'), account('a2', 'Approved')])
    renderApp('/app/owners/o1')

    expect(await screen.findByText('Pending approval')).toBeInTheDocument()
    expect(screen.getByText('Approved')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: 'Approve' })).toHaveLength(1)
    expect(screen.getAllByRole('button', { name: 'Reject' })).toHaveLength(1)
  })

  it('hides the decision buttons without Owners.ApproveBankAccounts', async () => {
    serve(['Owners.View', 'Owners.Manage'], [account('a1', 'PendingApproval')])
    renderApp('/app/owners/o1')

    expect(await screen.findByText('Pending approval')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Approve' })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Reject' })).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Add account' })).toBeInTheDocument()
  })

  it('explains the maker-checker refusal when the person who added the account approves it', async () => {
    serve(['Owners.View', 'Owners.ApproveBankAccounts'], [account('a1', 'PendingApproval', 'user-1')], {
      status: 403,
      body: { title: "The person who added a bank account can't approve or reject it.", code: 'maker_checker' },
    })
    renderApp('/app/owners/o1')

    expect(await screen.findByText(/You added this account/)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: 'Approve' }))
    const alert = await screen.findByRole('alert')
    expect(alert).toHaveTextContent("The person who added a bank account can't approve or reject it.")
    expect(alert).toHaveTextContent('Ask another finance approver')
  })

  it('checks the account number is a 10-digit NUBAN before saving', async () => {
    const calls = serve(['Owners.View', 'Owners.Manage'], [])
    renderApp('/app/owners/o1')
    await userEvent.click(await screen.findByRole('button', { name: 'Add account' }))
    await userEvent.type(screen.getByLabelText('Bank name'), 'GTBank')
    await userEvent.type(screen.getByLabelText('Account number'), '12345')
    await userEvent.type(screen.getByLabelText('Account name'), 'Okafor')
    await userEvent.click(screen.getAllByRole('button', { name: 'Add account' }).at(-1)!)
    expect(await screen.findByText('Account number must be 10 digits (NUBAN).')).toBeInTheDocument()
    expect(calls.some((c) => c.url.endsWith('/bank-accounts') && c.init.method === 'POST')).toBe(false)
  })
})
