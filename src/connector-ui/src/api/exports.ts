import { getToken } from './auth'

export interface ExportSummary {
  sequenceNo: number
  extractedAt: string
  recordCount: number
  sha256Short: string
  status: string
  dataFileName: string
  /** True when the run has been Pending for more than 24 hours. */
  isStale: boolean
}

export interface ExportDetail {
  id: number
  sequenceNo: number
  extractedAt: string
  recordCount: number
  sha256: string
  status: string
  releasedAt: string | null
  operatedBy: string | null
  approvedBy: string | null
  dataFileName: string
  // Delivery fields — null until POST …/deliver is called
  deliveredAt: string | null
  deliveredBy: string | null
  importedRecordCount: number | null
  deliveryNotes: string | null
  /** Non-null when a gap is detected between this run and the last released run. */
  sequenceGapWarning: string | null
}

export interface ReleaseRequest {
  approver: string
}

export interface DeliverRequest {
  importedRecordCount?: number | null
  notes?: string | null
}

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = { 'Content-Type': 'application/json' }
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

async function request<T>(url: string, init?: RequestInit): Promise<{ data: T; status: number }> {
  const res = await fetch(url, { headers: authHeaders(), ...init })
  const data = res.ok ? ((await res.json()) as T) : ({} as T)
  return { data, status: res.status }
}

export async function listExports(): Promise<ExportSummary[]> {
  const { data } = await request<ExportSummary[]>('/api/exports')
  return data
}

export async function getExport(seqNo: number): Promise<ExportDetail | null> {
  const { data, status } = await request<ExportDetail>(`/api/exports/${seqNo}`)
  return status === 404 ? null : data
}

export async function releaseExport(
  seqNo: number,
  body: ReleaseRequest,
): Promise<{ ok: boolean; status: number; message: string }> {
  const res = await fetch(`/api/exports/${seqNo}/release`, {
    method: 'POST',
    headers: authHeaders(),
    body: JSON.stringify(body),
  })
  const message = res.ok ? '' : await res.text()
  return { ok: res.ok, status: res.status, message }
}

export async function deliverExport(
  seqNo: number,
  body: DeliverRequest,
): Promise<{ ok: boolean; status: number; message: string }> {
  const res = await fetch(`/api/exports/${seqNo}/deliver`, {
    method: 'POST',
    headers: authHeaders(),
    body: JSON.stringify(body),
  })
  const message = res.ok ? '' : await res.text()
  return { ok: res.ok, status: res.status, message }
}
