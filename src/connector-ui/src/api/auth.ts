const TOKEN_KEY = 'connector_token'
const USER_KEY = 'connector_user'

function getToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function getUsername(): string | null {
  return sessionStorage.getItem(USER_KEY)
}

export function isLoggedIn(): boolean {
  return !!sessionStorage.getItem(TOKEN_KEY)
}

/** Authorization header for the stored session token, or no headers if not logged in. */
export function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

function storeSession(token: string, username: string): void {
  sessionStorage.setItem(TOKEN_KEY, token)
  sessionStorage.setItem(USER_KEY, username)
}

export function clearSession(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(USER_KEY)
}

/**
 * Invalidates every JWT issued to the current user, including the one this
 * request itself used — the caller must treat the local session as dead and re-login afterward regardless
 * of whether this call succeeds or fails, since a network error here shouldn't block signing out locally.
 */
export async function revokeAllSessions(): Promise<void> {
  await fetch('/api/auth/revoke-my-sessions', { method: 'POST', headers: authHeaders() }).catch(() => {})
}

/** GET /api/auth/me: the signed-in user, their role and their effective permissions (an Admin holds all). */
export interface CurrentUser {
  username: string
  role: 'Admin' | 'User'
  permissions: string[]
}

/** The signed-in user's role and permissions, or null when the session is no longer valid (401). Throws on any
 *  other failure, so a backend hiccup isn't mistaken for a dead session. */
export async function fetchCurrentUser(): Promise<CurrentUser | null> {
  const res = await fetch('/api/auth/me', { headers: authHeaders() })
  if (res.status === 401) return null
  if (!res.ok) throw new Error(`Error ${res.status}`)
  return (await res.json()) as CurrentUser
}

/** Changes the signed-in user's password. On success the server has signed this user out everywhere, this
 *  session included, so the caller must clear the session and send them to the login page. */
export async function changePassword(
  currentPassword: string,
  newPassword: string,
): Promise<{ ok: true } | { ok: false; error: string }> {
  const res = await fetch('/api/auth/change-password', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ currentPassword, newPassword }),
  })
  if (res.ok) return { ok: true }
  if (res.status === 429) return { ok: false, error: 'Too many attempts. Wait a minute and try again.' }
  return { ok: false, error: (await res.text().catch(() => '')) || `Error ${res.status}` }
}

export async function login(
  username: string,
  password: string,
): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch('/api/auth/login', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ username, password }),
  })
  if (res.ok) {
    const data = (await res.json()) as { token: string; username: string }
    storeSession(data.token, data.username)
    return { ok: true }
  }
  return { ok: false, error: res.status === 401 ? 'Invalid credentials.' : `Error ${res.status}` }
}
