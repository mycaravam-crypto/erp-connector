import { getToken } from './auth'

export interface SourceColumn {
  name: string
  type: string
  nullable: boolean
  primaryKey: boolean
}

export interface SourceTable {
  name: string
  description: string
  columns: SourceColumn[]
}

export interface SourceSchema {
  connectionLabel: string
  tables: SourceTable[]
}

/** Server-side stored connection — password is never returned. */
export interface ErpConnectionInfo {
  host: string
  port: number
  database: string
  username: string
}

/** Full config sent to POST /api/connection (password included, stays server-side). */
export interface ConnectionConfig extends ErpConnectionInfo {
  password: string
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

/** Returns the currently stored ERP connection (no password), or null if none is configured. */
export async function getConnection(): Promise<ErpConnectionInfo | null> {
  const res = await fetch('/api/connection', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ErpConnectionInfo>
}

/**
 * Tests the connection and, on success, persists it server-side and returns the live source schema.
 */
export async function saveConnection(
  cfg: ConnectionConfig,
): Promise<{ schema: SourceSchema } | { error: string; status: number }> {
  const res = await fetch('/api/connection', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json', ...authHeaders() },
    body: JSON.stringify({
      host: cfg.host,
      port: cfg.port,
      database: cfg.database,
      username: cfg.username,
      password: cfg.password,
    }),
  })
  if (res.ok) return { schema: (await res.json()) as SourceSchema }
  const text = await res.text().catch(() => '')
  return { error: text || `Server error (HTTP ${res.status})`, status: res.status }
}

/** Fetches the source schema using whatever connection is currently configured on the server. */
export async function getSourceSchema(): Promise<SourceSchema | null> {
  const res = await fetch('/api/source-schema', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<SourceSchema>
}
