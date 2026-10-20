/** A problem response (RFC 9457) from the API. */
export class ApiError extends Error {
  readonly status: number
  readonly code?: string
  readonly fieldErrors: Record<string, string[]>

  constructor(status: number, title: string, code?: string, fieldErrors: Record<string, string[]> = {}) {
    super(title)
    this.status = status
    this.code = code
    this.fieldErrors = fieldErrors
  }

  /** Every message in the problem, field errors included, for display. */
  get messages(): string[] {
    const fields = Object.values(this.fieldErrors).flat()
    return fields.length > 0 ? fields : [this.message]
  }
}

const ORGANIZATION_KEY = 'pms.organizationId'

/** The organization sent with each request. Remembered per browser for convenience only; the server checks it. */
export const organizationStore = {
  get(): string | null {
    try {
      return localStorage.getItem(ORGANIZATION_KEY)
    } catch {
      return null
    }
  },
  set(id: string | null) {
    try {
      if (id) localStorage.setItem(ORGANIZATION_KEY, id)
      else localStorage.removeItem(ORGANIZATION_KEY)
    } catch {
      // Storage can be unavailable (private mode); the selection then lasts for this page only.
    }
  },
}

type Method = 'GET' | 'POST' | 'PUT' | 'PATCH' | 'DELETE'

export async function api<T>(method: Method, path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = {
    Accept: 'application/json',
    // Required by the API on state-changing requests (CSRF protection).
    'X-Requested-With': 'XMLHttpRequest',
  }
  const organizationId = organizationStore.get()
  if (organizationId) headers['X-Organization-Id'] = organizationId
  if (body !== undefined) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (response.status === 204 || response.status === 202) return undefined as T
  const text = await response.text()
  const data = parseJson(text)

  if (!response.ok) {
    throw new ApiError(
      response.status,
      data?.title ?? `Request failed (${response.status})`,
      data?.code,
      normalizeErrors(data?.errors),
    )
  }
  return data as T
}

/** The body as JSON, or undefined when it is empty or not JSON (e.g. a proxy's error page). */
function parseJson(text: string) {
  if (!text) return undefined
  try {
    return JSON.parse(text)
  } catch {
    return undefined
  }
}

function normalizeErrors(errors: unknown): Record<string, string[]> {
  if (!errors || typeof errors !== 'object') return {}
  return Object.fromEntries(
    Object.entries(errors as Record<string, unknown>).map(([k, v]) => [k, Array.isArray(v) ? v.map(String) : [String(v)]]),
  )
}

export const get = <T,>(path: string) => api<T>('GET', path)
export const post = <T,>(path: string, body?: unknown) => api<T>('POST', path, body ?? {})
export const put = <T,>(path: string, body: unknown) => api<T>('PUT', path, body)
export const patch = <T,>(path: string, body: unknown) => api<T>('PATCH', path, body)
export const del = <T,>(path: string) => api<T>('DELETE', path)
