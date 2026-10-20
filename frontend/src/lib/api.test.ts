import { ApiError, api, organizationStore } from './api'
import { mockApi } from '../test/render'

describe('api client', () => {
  it('sends the CSRF header and the selected organization', async () => {
    organizationStore.set('org-42')
    const calls = mockApi(() => ({ status: 200, body: { ok: true } }))

    await api('POST', '/api/v1/branches', { name: 'Ikeja' })

    const headers = calls[0].init.headers as Record<string, string>
    expect(headers['X-Requested-With']).toBe('XMLHttpRequest')
    expect(headers['X-Organization-Id']).toBe('org-42')
    expect(calls[0].init.credentials).toBe('same-origin')
    expect(calls[0].init.body).toBe(JSON.stringify({ name: 'Ikeja' }))
  })

  it('omits the organization header when none is selected', async () => {
    const calls = mockApi(() => ({ status: 200, body: [] }))
    await api('GET', '/api/v1/organizations')
    expect((calls[0].init.headers as Record<string, string>)['X-Organization-Id']).toBeUndefined()
  })

  it('turns problem responses into ApiError with field messages', async () => {
    mockApi(() => ({
      status: 400,
      body: { title: 'One or more validation errors occurred.', errors: { PasswordTooShort: ['Passwords must be at least 10 characters.'] } },
    }))

    const error = (await api('POST', '/api/v1/auth/register', {}).catch((e: unknown) => e)) as ApiError

    expect(error).toBeInstanceOf(ApiError)
    expect(error.status).toBe(400)
    expect(error.messages).toEqual(['Passwords must be at least 10 characters.'])
  })

  it('keeps the problem code', async () => {
    mockApi(() => ({ status: 409, body: { title: 'An organization must keep at least one Organization Admin.', code: 'last_admin' } }))
    const error = (await api('DELETE', '/api/v1/organizations/x/members/y').catch((e: unknown) => e)) as ApiError
    expect(error.code).toBe('last_admin')
    expect(error.messages).toEqual(['An organization must keep at least one Organization Admin.'])
  })
})

describe('api client with a non-JSON error', () => {
  it('still raises an ApiError with the status', async () => {
    globalThis.fetch = vi.fn(async () => new Response('<html>Bad gateway</html>', { status: 502 })) as typeof fetch
    const error = (await api('GET', '/api/v1/owners').catch((e: unknown) => e)) as ApiError
    expect(error).toBeInstanceOf(ApiError)
    expect(error.messages).toEqual(['Request failed (502)'])
  })
})
