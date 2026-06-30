import { getToken } from './auth'

export interface SchedulerConfig {
  scheduledTimeUtc: string // HH:mm
  retentionDays: number
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
