import { authHeaders } from './auth'

/** One login, as listed in Settings → Users (Admin only). */
export interface UserAccount {
  username: string
  role: 'Admin' | 'User'
  createdAt: string
}

type ApiResult<T> = { ok: true; data: T } | { ok: false; error: string }

async function send<T>(url: string, method: string, body?: unknown): Promise<ApiResult<T>> {
  const res = await fetch(url, {
    method,
    headers: body === undefined ? authHeaders() : { 'Content-Type': 'application/json', ...authHeaders() },
    body: body === undefined ? undefined : JSON.stringify(body),
  })
  if (res.ok) return { ok: true, data: (res.status === 204 ? undefined : await res.json()) as T }
  return { ok: false, error: (await res.text().catch(() => '')) || `Error ${res.status}` }
}

export async function listUsers(): Promise<UserAccount[]> {
  const result = await send<UserAccount[]>('/api/users', 'GET')
  if (!result.ok) throw new Error(result.error)
  return result.data
}

export function createUser(username: string, password: string, role: string): Promise<ApiResult<UserAccount>> {
  return send<UserAccount>('/api/users', 'POST', { username, password, role })
}

/** Changes the role and/or resets the password; a field left undefined stays as it is. */
export function updateUser(
  username: string,
  changes: { role?: string; password?: string },
): Promise<ApiResult<UserAccount>> {
  return send<UserAccount>(`/api/users/${encodeURIComponent(username)}`, 'PUT', changes)
}

export function deleteUser(username: string): Promise<ApiResult<void>> {
  return send<void>(`/api/users/${encodeURIComponent(username)}`, 'DELETE')
}
