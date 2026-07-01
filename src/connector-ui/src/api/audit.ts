import { getToken } from './auth'

export interface AuditEntry {
  id: number
  timestamp: string
  username: string
  action: string
  detail: string | null
}

export interface GdprDeniedFieldsResult {
  fields: string[]
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

export async function getAuditLog(limit = 100): Promise<AuditEntry[]> {
  const res = await fetch(`/api/audit?limit=${limit}`, { headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to load audit log (HTTP ${res.status})`)
  return res.json() as Promise<AuditEntry[]>
}

export async function getGdprDeniedFields(): Promise<GdprDeniedFieldsResult> {
  const res = await fetch('/api/gdpr-denied-fields', { headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to load GDPR denied fields (HTTP ${res.status})`)
  return res.json() as Promise<GdprDeniedFieldsResult>
}

export async function saveGdprDeniedFields(
  fields: string[],
): Promise<{ ok: boolean; error?: string }> {
  const res = await fetch('/api/gdpr-denied-fields', {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({ fields }),
  })
  if (res.ok) return { ok: true }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Server error (HTTP ${res.status})` }
}
