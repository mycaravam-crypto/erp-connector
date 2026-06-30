import { getToken } from './auth'

export interface RunNowResult {
  sequenceNo: number
  recordCount: number
  sha256Short: string
}

export interface PreviewResult {
  recordCount: number
  schemaVersion: string
  /** Ordered column names — same keys used in each record dictionary. */
  columns: string[]
  /** Each record is a key→value map keyed by target column name. */
  records: Record<string, string>[]
  /** "dynamic" = live Postgres via export_mapping; "error" = no mapping/connection configured or query failed. */
  source: 'dynamic' | 'error'
  /** Source table name when source is "dynamic" or "error". */
  sourceTable?: string
  /** Set when source === "error" — describes what went wrong. */
  error?: string
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

export async function runNow(
  format: 'xlsx' | 'csv' | 'json' = 'xlsx',
): Promise<{ ok: boolean; data?: RunNowResult; error?: string }> {
  const res = await fetch(`/api/pipeline/run?format=${format}`, {
    method: 'POST',
    headers: authHeaders(),
  })
  if (res.ok) {
    const data = (await res.json()) as RunNowResult
    return { ok: true, data }
  }
  try {
    const json = (await res.json()) as { detail?: string; title?: string }
    return { ok: false, error: json.detail || json.title || `Export failed (HTTP ${res.status})` }
  } catch {
    return { ok: false, error: `Export failed (HTTP ${res.status})` }
  }
}

export async function getPreview(): Promise<PreviewResult | null> {
  const res = await fetch('/api/pipeline/preview', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<PreviewResult>
}
