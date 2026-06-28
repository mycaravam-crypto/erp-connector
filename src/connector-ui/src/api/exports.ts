import { getToken } from './auth'

export interface ExportSummary {
  sequenceNo: number
  extractedAt: string
  recordCount: number
  sha256Short: string
  status: string
  dataFileName: string
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
}

export interface ReleaseRequest {
  approver: string
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
