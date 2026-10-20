import { screen } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { me, mockApi, renderApp } from '../../test/render'

const tenant = {
  id: 't1',
  type: 'Individual',
  name: 'Ngozi Eze',
  email: 'ngozi@example.com',
  phone: null,
  alternatePhone: null,
  address: null,
  employer: null,
  occupation: null,
  emergencyContactName: null,
  emergencyContactPhone: null,
  companyRegistrationNumber: null,
  taxIdentificationNumber: null,
  notes: null,
  createdAtUtc: '2026-10-01T09:00:00',
  kyc: { idType: 'Nin', idNumberLast4: '5678', bvnLast4: '4321', status: 'Unverified', verifiedAtUtc: null, verifiedByName: null },
  hasPortalAccess: false,
}

const org = (permissions: string[]) => ({
  id: 'org-1',
  name: 'Lekki Homes Ltd',
  slug: 'lekki',
  type: 'ManagementCompany',
  status: 'Active',
  roles: ['PropertyManager'],
  permissions: ['Organization.View', ...permissions],
})

function serve(permissions: string[]) {
  return mockApi((url) => {
    if (url.endsWith('/auth/me')) return { status: 200, body: me([org(permissions)]) }
    if (url.endsWith('/tenants/t1')) return { status: 200, body: tenant }
    if (url.endsWith('/tenants/t1/kyc')) return { status: 200, body: { ...tenant.kyc, idNumber: '12345675678', bvn: '22222224321' } }
    return { status: 200, body: [] }
  })
}

describe('tenant KYC panel', () => {
  it('shows only the last four digits and no reveal button without Tenants.ViewKyc', async () => {
    const calls = serve(['Tenants.View', 'Tenants.Update'])
    renderApp('/app/tenants/t1')

    expect(await screen.findByText('•••• 5678')).toBeInTheDocument()
    expect(screen.getByText('•••• 4321')).toBeInTheDocument()
    expect(screen.getByText('NIN')).toBeInTheDocument()
    expect(screen.getByText('Unverified')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Show full details' })).not.toBeInTheDocument()
    expect(screen.queryByLabelText('Verification')).not.toBeInTheDocument()
    // Editing is still allowed with Tenants.Update.
    expect(screen.getByRole('button', { name: 'Update ID' })).toBeInTheDocument()
    expect(calls.some((c) => c.url.endsWith('/kyc'))).toBe(false)
  })

  it('reveals the full numbers on request with Tenants.ViewKyc', async () => {
    serve(['Tenants.View', 'Tenants.ViewKyc'])
    renderApp('/app/tenants/t1')

    await userEvent.click(await screen.findByRole('button', { name: 'Show full details' }))
    expect(await screen.findByText('12345675678')).toBeInTheDocument()
    expect(screen.getByText('22222224321')).toBeInTheDocument()
    expect(screen.getByLabelText('Verification')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Update ID' })).not.toBeInTheDocument()
  })

  it('checks NIN and BVN length before saving', async () => {
    const calls = serve(['Tenants.View', 'Tenants.Update'])
    renderApp('/app/tenants/t1')
    await userEvent.click(await screen.findByRole('button', { name: 'Update ID' }))
    await userEvent.type(screen.getByLabelText('ID number'), '1234')
    await userEvent.type(screen.getByLabelText('BVN'), '99')
    await userEvent.click(screen.getByRole('button', { name: 'Save ID' }))

    expect(await screen.findByText('A NIN is 11 digits.')).toBeInTheDocument()
    expect(screen.getByText('A BVN is 11 digits.')).toBeInTheDocument()
    expect(calls.some((c) => c.init.method === 'PUT')).toBe(false)
  })
})
