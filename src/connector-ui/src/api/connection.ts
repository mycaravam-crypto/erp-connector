import { authHeaders } from './auth'

export interface SourceColumn {
  name: string
  type: string
  nullable: boolean
  primaryKey: boolean
  foreignKeyTable: string | null
  foreignKeyColumn: string | null
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

/** Backend `DataSourceType` (serialized as its numeric value). */
export const DataSourceType = {
  PostgreSql: 0,
  MariaDb: 1,
  ServiceNowTableApi: 2,
  ServiceNowSqlApi: 3,
} as const
export type DataSourceType = (typeof DataSourceType)[keyof typeof DataSourceType]

/** Server-side stored connection — password is never returned, only whether one is set. Host/port/database
 *  are set for PostgreSQL/MariaDB, instanceUrl for ServiceNow. A config stored before `type` existed comes
 *  back as PostgreSQL (0). */
export interface ErpConnectionInfo {
  type?: DataSourceType
  host: string | null
  port: number | null
  database: string | null
  instanceUrl?: string | null
  username: string
  /** One of Npgsql's SslMode names (Disable/Allow/Prefer/Require/VerifyCA/VerifyFull), or null/empty to
   *  use the default (Prefer — falls back to unencrypted if the server doesn't offer TLS).
   *  MariaDB uses the same names. */
  sslMode: string | null
  hasPassword?: boolean
}

/** Full config sent to POST /api/connection (password included, stays server-side). An empty password
 *  keeps the stored one, as long as type, target and username are unchanged. */
export interface ConnectionConfig {
  type: DataSourceType
  host: string | null
  port: number | null
  database: string | null
  instanceUrl: string | null
  username: string
  password: string
  sslMode: string | null
}

/** Returns the currently stored ERP connection (no password), or null if none is configured. */
export async function getConnection(): Promise<ErpConnectionInfo | null> {
  const res = await fetch('/api/connection', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<ErpConnectionInfo>
}

// ── Connection status cache (for route guards) ────────────────────────────────

let _connectionConfigured: boolean | null = null

/** Call after saving a new connection so the route guard re-checks next navigation. */
export function invalidateConnectionCache(): void {
  _connectionConfigured = null
}

/** True if a connection has been configured server-side. Result is cached for the session. */
export async function isConnectionConfigured(): Promise<boolean> {
  if (_connectionConfigured !== null) return _connectionConfigured
  const conn = await getConnection()
  _connectionConfigured = conn !== null
  return _connectionConfigured
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
    body: JSON.stringify({ ...cfg, sslMode: cfg.sslMode || null }),
  })
  if (res.ok) return { schema: (await res.json()) as SourceSchema }
  const text = await res.text().catch(() => '')
  return { error: text || `Server error (HTTP ${res.status})`, status: res.status }
}

/**
 * Fetches the source schema using whatever connection is currently configured on the server.
 * Throws (with the server's error detail, when available) if the configured connection can't
 * be reached or introspected — the caller should surface this rather than silently falling back.
 */
export async function getSourceSchema(): Promise<SourceSchema> {
  const res = await fetch('/api/source-schema', { headers: authHeaders() })
  if (!res.ok) {
    const text = await res.text().catch(() => '')
    let detail = text
    try {
      detail = JSON.parse(text)?.detail || text
    } catch {
      // not JSON — use the raw text
    }
    throw new Error(detail || `Server error (HTTP ${res.status})`)
  }
  return res.json() as Promise<SourceSchema>
}
