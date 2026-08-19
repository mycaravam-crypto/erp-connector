import { getToken } from './auth'

export interface RunNowResult {
  sequenceNo: number
  recordCount: number
  sha256Short: string
}

export interface PreviewResult {
  recordCount: number
  schemaVersion: string
  /** Ordered column names — same keys used in each record dictionary. Empty for a nested mapping. */
  columns: string[]
  /** Each record is a key→value map keyed by target column name. Empty for a nested mapping. */
  records: Record<string, string>[]
  /**
   * "dynamic" = flat mapping via live Postgres; "dynamic-nested" = mapping has nested JSON groups, see
   * nestedRecords instead; "error" = no mapping/connection configured or query failed.
   */
  source: 'dynamic' | 'dynamic-nested' | 'error'
  /** Source table name when source is "dynamic"/"dynamic-nested" or "error". */
  sourceTable?: string
  /** Set when source === "error" — describes what went wrong. */
  error?: string
  /** Populated instead of columns/records when source === "dynamic-nested" — arbitrary nested JSON shape. */
  nestedRecords?: unknown[]
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
