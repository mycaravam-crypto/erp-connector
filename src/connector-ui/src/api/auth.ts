export const TOKEN_KEY = 'connector_token'
export const USER_KEY = 'connector_user'

export function getToken(): string | null {
  return sessionStorage.getItem(TOKEN_KEY)
}

export function getUsername(): string | null {
  return sessionStorage.getItem(USER_KEY)
}

export function isLoggedIn(): boolean {
  return !!sessionStorage.getItem(TOKEN_KEY)
}

export function storeSession(token: string, username: string): void {
  sessionStorage.setItem(TOKEN_KEY, token)
  sessionStorage.setItem(USER_KEY, username)
}

export function clearSession(): void {
  sessionStorage.removeItem(TOKEN_KEY)
  sessionStorage.removeItem(USER_KEY)
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
