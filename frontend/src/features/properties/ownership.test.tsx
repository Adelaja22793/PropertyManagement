import { screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { me, mockApi, renderApp } from '../../test/render'
import { ownershipProblem, ownershipTotal } from './ownership'

describe('ownership share rules', () => {
  it('sums in hundredths so thirds can make exactly 100', () => {
    const rows = [
      { ownerId: 'a', percent: '33.33' },
      { ownerId: 'b', percent: '33.33' },
      { ownerId: 'c', percent: '33.34' },
    ]
    expect(ownershipTotal(rows)).toBe(100)
    expect(ownershipProblem(rows)).toBeNull()
  })

  it('explains what is wrong until the shares are valid', () => {
    expect(ownershipProblem([{ ownerId: 'a', percent: '60' }])).toMatch(/total exactly 100%/)
    expect(ownershipProblem([{ ownerId: '', percent: '100' }])).toMatch(/Choose an owner/)
    expect(
      ownershipProblem([
        { ownerId: 'a', percent: '50' },
        { ownerId: 'a', percent: '50' },
      ]),
    ).toMatch(/only once/)
    expect(ownershipProblem([{ ownerId: 'a', percent: '100.001' }])).toMatch(/two decimals/)
    expect(ownershipProblem([{ ownerId: 'a', percent: '0' }])).toMatch(/more than 0/)
  })
})

const managerOrg = {
  id: 'org-1',
  name: 'Lekki Homes Ltd',
  slug: 'lekki',
  type: 'ManagementCompany',
  status: 'Active',
  roles: ['OrganizationAdmin'],
  permissions: ['Organization.View', 'Properties.View', 'Properties.Update', 'Owners.View'],
}

const property = {
  id: 'p1',
  code: 'LKK-01',
  name: 'Palm Court',
  type: 'Residential',
  addressLine: '12 Admiralty Way',
  city: 'Lekki',
  state: 'Lagos',
  branchId: null,
  branchName: null,
  managerUserId: null,
  managerName: null,
  status: 'Active',
  unitCount: 0,
  occupiedUnitCount: 0,
  description: null,
  localGovernmentArea: null,
  amenities: null,
  createdAtUtc: '2026-10-01T09:00:00',
  archivedAtUtc: null,
  currentOwners: [],
}

describe('ownership editor', () => {
  it('shows the running total and enables saving only at exactly 100%', async () => {
    const calls = mockApi((url, init) => {
      if (url.endsWith('/auth/me')) return { status: 200, body: me([managerOrg]) }
      if (url.endsWith('/properties/p1')) return { status: 200, body: property }
      if (url.includes('/properties/p1/units')) return { status: 200, body: [] }
      if (url.endsWith('/properties/p1/owners') && init.method === 'PUT') return { status: 200, body: { current: [], history: [] } }
      if (url.endsWith('/properties/p1/owners')) return { status: 200, body: { current: [], history: [] } }
      if (url.includes('/api/v1/owners')) {
        return {
          status: 200,
          body: {
            items: [
              { id: 'o1', name: 'Chief Okafor' },
              { id: 'o2', name: 'Mrs Bello' },
            ],
            page: 1,
            pageSize: 100,
            totalCount: 2,
          },
        }
      }
      return { status: 200, body: [] }
    })
    renderApp('/app/properties/p1')

    await userEvent.click(await screen.findByRole('button', { name: 'Set owners' }))
    const save = screen.getByRole('button', { name: 'Save owners' })
    await screen.findByRole('option', { name: 'Chief Okafor' })

    await userEvent.selectOptions(screen.getByLabelText('Owner 1'), 'o1')
    await userEvent.clear(screen.getByLabelText('Share %'))
    await userEvent.type(screen.getByLabelText('Share %'), '60')
    expect(screen.getByTestId('ownership-total')).toHaveTextContent('Total: 60%')
    expect(save).toBeDisabled()

    await userEvent.click(screen.getByRole('button', { name: 'Add another owner' }))
    await userEvent.selectOptions(screen.getByLabelText('Owner 2'), 'o2')
    const shares = screen.getAllByLabelText('Share %')
    await userEvent.type(shares[1], '40.5')
    expect(screen.getByTestId('ownership-total')).toHaveTextContent('Total: 100.5%')
    expect(save).toBeDisabled()

    await userEvent.clear(shares[1])
    await userEvent.type(shares[1], '40')
    expect(screen.getByTestId('ownership-total')).toHaveTextContent('Total: 100%')
    expect(save).toBeEnabled()

    await userEvent.click(save)
    const put = calls.find((c) => c.init.method === 'PUT')!
    const body = JSON.parse(put.init.body as string)
    expect(body.shares).toEqual([
      { ownerId: 'o1', sharePercent: 60 },
      { ownerId: 'o2', sharePercent: 40 },
    ])
    expect(body.effectiveFrom).toMatch(/^\d{4}-\d{2}-\d{2}$/)
  })

  it('hides the editor without permission to change owners', async () => {
    mockApi((url) => {
      if (url.endsWith('/auth/me')) return { status: 200, body: me([{ ...managerOrg, permissions: ['Organization.View', 'Properties.View'] }]) }
      if (url.endsWith('/properties/p1')) return { status: 200, body: property }
      if (url.endsWith('/properties/p1/owners')) {
        return {
          status: 200,
          body: {
            current: [{ ownerId: 'o1', ownerName: 'Chief Okafor', sharePercent: 100, effectiveFrom: '2026-01-01', effectiveTo: null }],
            history: [],
          },
        }
      }
      return { status: 200, body: [] }
    })
    renderApp('/app/properties/p1')
    const owners = (await screen.findByText('Chief Okafor')).closest('section')!
    expect(within(owners).getByText('100%')).toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /owners/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: 'Edit' })).not.toBeInTheDocument()
  })
})
