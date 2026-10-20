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
  const isForm = body instanceof FormData
  if (body !== undefined && !isForm) headers['Content-Type'] = 'application/json'

  const response = await fetch(path, {
    method,
    headers,
    credentials: 'same-origin',
    body: body === undefined ? undefined : isForm ? body : JSON.stringify(body),
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
/** Sends a file upload (multipart form). The browser sets the content type and boundary. */
export const upload = <T,>(path: string, form: FormData) => api<T>('POST', path, form)

/**
 * The address of a stored document. The organization is part of the path, so the link works in an image or a new
 * tab; the server checks the signed-in user's access on every request.
 */
export function documentUrl(id: string, download = false): string {
  const organizationId = organizationStore.get() ?? ''
  return `/api/v1/organizations/${organizationId}/documents/${id}/content${download ? '?download=true' : ''}`
}

/**
 * Calls to the public site's API (listings and enquiries). They never carry the signed-in user's organization: the
 * listing's own address decides which company's data is shown.
 */
async function publicApi<T>(method: 'GET' | 'POST', path: string, body?: unknown): Promise<T> {
  const headers: Record<string, string> = { Accept: 'application/json', 'X-Requested-With': 'XMLHttpRequest' }
  if (body !== undefined) headers['Content-Type'] = 'application/json'
  const response = await fetch(path, { method, headers, credentials: 'omit', body: body === undefined ? undefined : JSON.stringify(body) })
  if (response.status === 204 || response.status === 202) return undefined as T
  const data = parseJson(await response.text())
  if (!response.ok) throw new ApiError(response.status, data?.title ?? `Request failed (${response.status})`, data?.code, normalizeErrors(data?.errors))
  return data as T
}

export const publicGet = <T,>(path: string) => publicApi<T>('GET', path)
export const publicPost = <T,>(path: string, body: unknown) => publicApi<T>('POST', path, body)

/** Downloads a file from the API (with the organization header) and hands it to the browser to save. */
export async function downloadFile(path: string, fallbackName: string): Promise<void> {
  const headers: Record<string, string> = { 'X-Requested-With': 'XMLHttpRequest' }
  const organizationId = organizationStore.get()
  if (organizationId) headers['X-Organization-Id'] = organizationId
  const response = await fetch(path, { headers, credentials: 'same-origin' })
  if (!response.ok) {
    const data = parseJson(await response.text())
    throw new ApiError(response.status, data?.title ?? `Download failed (${response.status})`, data?.code, normalizeErrors(data?.errors))
  }
  const name = /filename="?([^";]+)"?/.exec(response.headers.get('Content-Disposition') ?? '')?.[1] ?? fallbackName
  const url = URL.createObjectURL(await response.blob())
  const link = document.createElement('a')
  link.href = url
  link.download = name
  document.body.appendChild(link)
  link.click()
  link.remove()
  setTimeout(() => URL.revokeObjectURL(url), 1000)
}
