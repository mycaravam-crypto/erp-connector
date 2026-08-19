import { getToken } from './auth'

export interface SchedulerConfig {
  scheduledTimeUtc: string // HH:mm
  retentionDays: number
  format: 'xlsx' | 'csv' | 'json'
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

export async function getSchedulerConfig(): Promise<SchedulerConfig> {
  const res = await fetch('/api/settings/scheduler', { headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to load scheduler config (HTTP ${res.status})`)
  return res.json() as Promise<SchedulerConfig>
}

export async function saveSchedulerConfig(
  cfg: SchedulerConfig,
): Promise<{ ok: true } | { ok: false; error: string }> {
  const res = await fetch('/api/settings/scheduler', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify(cfg),
  })
  if (res.ok) return { ok: true }
  const text = await res.text().catch(() => '')
  return { ok: false, error: text || `Server error (HTTP ${res.status})` }
}

export interface GdprDeniedFieldsResult {
  fields: string[]
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
