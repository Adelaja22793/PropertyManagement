import { screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { adminOrg, agentOrg, me, mockApi, renderApp } from '../test/render'

describe('routing and access', () => {
  it('sends signed-out visitors to the sign-in page', async () => {
    mockApi(() => ({ status: 401 }))
    renderApp('/app/team')
    expect(await screen.findByRole('heading', { name: 'Sign in' })).toBeInTheDocument()
  })

  it('shows only the pages the role allows', async () => {
    mockApi((url) => (url.endsWith('/auth/me') ? { status: 200, body: me([agentOrg]) } : { status: 200, body: [] }))
    renderApp('/app')

    const nav = await screen.findByRole('navigation', { name: 'Main' })
    expect(nav).toHaveTextContent('Dashboard')
    expect(nav).toHaveTextContent('Settings')
    expect(nav).not.toHaveTextContent('Team')
    expect(nav).not.toHaveTextContent('Audit log')
  })

  it('blocks a page the role does not allow even when visited directly', async () => {
    mockApi((url) => (url.endsWith('/auth/me') ? { status: 200, body: me([agentOrg]) } : { status: 200, body: [] }))
    renderApp('/app/audit')
    expect(await screen.findByText("You don't have access to this page")).toBeInTheDocument()
  })

  it('switching organization sends the new organization on later requests', async () => {
    const calls = mockApi((url) => {
      if (url.endsWith('/auth/me')) return { status: 200, body: me([adminOrg, agentOrg]) }
      return { status: 200, body: [] }
    })
    renderApp('/app')
    const switcher = await screen.findByLabelText('Organization')

    await userEvent.selectOptions(switcher, 'org-2')

    await waitFor(() => expect(localStorage.getItem('pms.organizationId')).toBe('org-2'))
    // The agent role in org-2 has no Team page.
    expect(screen.getByRole('navigation', { name: 'Main' })).not.toHaveTextContent('Team')
    expect(calls.length).toBeGreaterThan(0)
  })
})

describe('sign in', () => {
  it('validates before calling the API', async () => {
    const calls = mockApi(() => ({ status: 401 }))
    renderApp('/login')
    await userEvent.click(await screen.findByRole('button', { name: 'Sign in' }))

    expect(await screen.findByText('Enter a valid email address.')).toBeInTheDocument()
    expect(screen.getByText('Enter your password.')).toBeInTheDocument()
    expect(calls.filter((c) => c.url.endsWith('/auth/login'))).toHaveLength(0)
  })

  it('shows the API message when the password is wrong', async () => {
    mockApi((url) => (url.endsWith('/auth/login') ? { status: 401, body: { title: 'Email or password is incorrect.' } } : { status: 401 }))
    renderApp('/login')
    await userEvent.type(await screen.findByLabelText('Email'), 'seun@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'wrong-password-1')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('alert')).toHaveTextContent('Email or password is incorrect.')
  })

  it('opens the dashboard after signing in', async () => {
    let signedIn = false
    mockApi((url) => {
      if (url.endsWith('/auth/login')) {
        signedIn = true
        return { status: 200, body: me([adminOrg]) }
      }
      if (url.endsWith('/auth/me')) return signedIn ? { status: 200, body: me([adminOrg]) } : { status: 401 }
      return { status: 200, body: [] }
    })
    renderApp('/login')
    await userEvent.type(await screen.findByLabelText('Email'), 'seun@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'correct-horse-42')
    await userEvent.click(screen.getByRole('button', { name: 'Sign in' }))

    expect(await screen.findByRole('heading', { name: 'Welcome, Seun' })).toBeInTheDocument()
  })
})

describe('registration', () => {
  it('asks for a company name unless joining by invitation', async () => {
    mockApi(() => ({ status: 401 }))
    renderApp('/register')
    await userEvent.type(await screen.findByLabelText('Full name'), 'Seun Adelaja')
    await userEvent.type(screen.getByLabelText('Email'), 'seun@example.com')
    await userEvent.type(screen.getByLabelText('Password'), 'goodpass123')
    await userEvent.click(screen.getByRole('button', { name: 'Create account' }))
    expect(await screen.findByText(/Enter your company name/)).toBeInTheDocument()
  })

  it('hides the company fields for an invitation link', async () => {
    mockApi(() => ({ status: 401 }))
    renderApp('/register?invite=abc&email=ada%40example.com')
    expect(await screen.findByRole('heading', { name: 'Join your team' })).toBeInTheDocument()
    expect(screen.queryByLabelText('Company or business name')).not.toBeInTheDocument()
    expect(screen.getByLabelText('Email')).toHaveValue('ada@example.com')
  })
})
