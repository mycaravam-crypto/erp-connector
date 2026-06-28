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
  operator: string
  approver: string
}

async function request<T>(url: string, init?: RequestInit): Promise<{ data: T; status: number }> {
  const res = await fetch(url, { headers: { 'Content-Type': 'application/json' }, ...init })
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
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body),
  })
  const message = res.ok ? '' : await res.text()
  return { ok: res.ok, status: res.status, message }
}
