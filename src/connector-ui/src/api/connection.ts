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

function authHeaders(): Record<string, string> {
  const token = getToken()
  const headers: Record<string, string> = {}
  if (token) headers['Authorization'] = `Bearer ${token}`
  return headers
}

export async function getSourceSchema(): Promise<SourceSchema | null> {
  const res = await fetch('/api/source-schema', { headers: authHeaders() })
  if (!res.ok) return null
  return res.json() as Promise<SourceSchema>
}
