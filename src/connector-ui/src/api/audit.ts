import { authHeaders } from './auth'

export interface AuditEntry {
  id: number
  timestamp: string
  username: string
  action: string
  detail: string | null
}

export async function getAuditLog(limit = 100): Promise<AuditEntry[]> {
  const res = await fetch(`/api/audit?limit=${limit}`, { headers: authHeaders() })
  if (!res.ok) throw new Error(`Failed to load audit log (HTTP ${res.status})`)
  return res.json() as Promise<AuditEntry[]>
}
