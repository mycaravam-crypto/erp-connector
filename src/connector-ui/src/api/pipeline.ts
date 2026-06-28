import { getToken } from './auth'

export interface RunNowResult {
  sequenceNo: number
  recordCount: number
  sha256Short: string
}

export interface PreviewRecord {
  guid: string
  serialNumber: string
  partNumber: string
  parentSerialNumber: string | null
  modelReference: string
  commissioningDate: string
  maintenanceState: string
}

export interface PreviewResult {
  recordCount: number
  schemaVersion: string
  records: PreviewRecord[]
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
  const text = await res.text()
  return { ok: false, error: text || `Error ${res.status}` }
}

export async function getPreview(): Promise<PreviewResult | null> {
  const res = await fetch('/api/pipeline/preview', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<PreviewResult>
}
